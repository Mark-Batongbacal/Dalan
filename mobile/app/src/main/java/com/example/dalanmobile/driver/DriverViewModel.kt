package com.example.dalanmobile.driver

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.room.Room
import androidx.room.withTransaction
import java.io.IOException
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex

data class DriverWorkspace(
    val signedIn: Boolean = false,
    val gpsProofEnabled: Boolean = false,
    val tasks: List<TaskInfo> = emptyList(),
    val events: List<PendingEvent> = emptyList(),
    val busy: Boolean = false,
    val message: String? = null,
    val needsLogin: Boolean = false,
    val driverId: String? = null,
    val syncing: Boolean = false,
    val lastSyncedAt: Instant? = null,
)

private data class SyncProgress(val active: Boolean = false, val owner: String? = null, val refreshedAt: Instant? = null)

@OptIn(ExperimentalCoroutinesApi::class)
class DriverViewModel(application: Application) : AndroidViewModel(application) {
    private val database = Room.databaseBuilder(application, DriverDatabase::class.java, "driver.db").build()
    private val dao = database.driverDao()
    private val api = DriverApi()
    private val sessions = SessionStore(application)
    private val session = MutableStateFlow(sessions.load())
    private val busy = MutableStateFlow(false)
    private val message = MutableStateFlow<String?>(null)
    private val needsLogin = MutableStateFlow(false)
    private val syncProgress = MutableStateFlow(SyncProgress())
    private val operation = Mutex()

    private val workspace = session.flatMapLatest { account ->
        if (account == null) flowOf(DriverWorkspace())
        else combine(dao.tasks(account.driverId), dao.events(account.driverId)) { tasks, events ->
            val saved = tasks.map { driverJson.decodeFromString<TaskInfo>(it.payload) }
            DriverWorkspace(signedIn = true, tasks = saved.sortedWith(compareBy<TaskInfo> { Instant.parse(it.assignedAtUtc) }
                .thenBy { it.routeId }
                .thenBy {
                    if (it.status == "PickedUp" || events.any { event ->
                            event.deliveryId == it.id && event.assignment == it.assignedAtUtc &&
                                event.kind == "Pickup" && event.failure == null
                        }) it.dropoffPosition else it.pickupPosition
                }), events = events, driverId = account.driverId, gpsProofEnabled = account.gpsProofEnabled)
        }
    }
    val state = combine(workspace, busy, message, needsLogin, syncProgress) { data, working, notice, expired, progress ->
        data.copy(busy = working, message = notice, needsLogin = expired,
            syncing = progress.active && progress.owner == data.driverId,
            lastSyncedAt = progress.refreshedAt.takeIf { progress.owner == data.driverId })
    }.stateIn(viewModelScope, SharingStarted.Eagerly, DriverWorkspace(signedIn = session.value != null))

    fun login(email: String, password: String) = runOperation {
        val signedIn = api.login(email, password)
        if (signedIn.driverId != session.value?.driverId) DriverLocationService.stop(getApplication())
        sessions.save(signedIn)
        session.value = signedIn
        needsLogin.value = false
        synchronize(signedIn)
    }

    fun logout() = runOperation {
        DriverLocationService.stop(getApplication())
        sessions.clear()
        session.value = null
        needsLogin.value = false
        syncProgress.value = SyncProgress()
    }

    fun sync() = runOperation {
        if (!needsLogin.value) session.value?.let { synchronize(it) }
    }

    fun record(task: TaskInfo, kind: String, latitude: Double? = null, longitude: Double? = null,
               confirmation: String? = null, photo: String? = null,
               locationAccuracyMeters: Double? = null, locationCapturedAtUtc: String? = null,
               gpsExceptionReason: String? = null) = runOperation {
        val account = session.value ?: return@runOperation
        val event = TaskEvent(UUID.randomUUID().toString(), task.assignedAtUtc,
            Instant.now().toString(), kind, latitude, longitude, confirmation, photo,
            locationAccuracyMeters, locationCapturedAtUtc, gpsExceptionReason)
        val added = database.withTransaction {
            val stored = dao.task(account.driverId, task.id)?.let {
                driverJson.decodeFromString<TaskInfo>(it.payload)
            }
            val pending = dao.queued(account.driverId).filter {
                it.deliveryId == task.id && it.assignment == task.assignedAtUtc
            }
            val pickedUp = stored?.status == "PickedUp" || pending.any { it.kind == "Pickup" }
            if (stored?.assignedAtUtc != task.assignedAtUtc || pending.any { it.failure != null } ||
                pending.any { it.kind == kind } || pickedUp != (kind == "Deliver")) {
                false
            } else {
                dao.enqueue(PendingEvent(owner = account.driverId, deliveryId = task.id,
                    assignment = task.assignedAtUtc, kind = kind, payload = driverJson.encodeToString(event)))
                true
            }
        }
        if (!added) message.value = "The task changed. Refresh and try again."
        else if (!needsLogin.value) synchronize(account)
    }

    suspend fun route(deliveryId: String): DriverRoadRoute {
        val account = session.value ?: throw DriverHttpException(401)
        return api.route(account, deliveryId)
    }

    suspend fun navigate(location: android.location.Location): DriverNavigationRoute? {
        val account = session.value ?: throw DriverHttpException(401)
        return api.navigate(account, DriverNavigationRequest(NavigationPoint(location.latitude, location.longitude),
            Instant.ofEpochMilli(location.time).toString(), if (location.hasAccuracy()) location.accuracy.toDouble() else 10000.0,
            includeRemainingStops = true))
    }

    fun discard(event: PendingEvent) = runOperation {
        val account = session.value ?: return@runOperation
        dao.discard(account.driverId, event.deliveryId, event.assignment)
        if (!needsLogin.value) synchronize(account)
    }

    fun requestGpsReview(entry: PendingEvent, reason: String) = runOperation {
        val account = session.value ?: return@runOperation
        if (entry.owner != account.driverId || entry.kind != "Deliver" || reason.trim().length !in 8..500) return@runOperation
        val current = dao.queued(account.driverId).firstOrNull { it.sequence == entry.sequence } ?: return@runOperation
        val proof = driverJson.decodeFromString<TaskEvent>(current.payload)
        if (proof.gpsExceptionReason != null) return@runOperation
        dao.requestReview(entry.sequence, account.driverId, driverJson.encodeToString(proof.copy(gpsExceptionReason = reason.trim())))
        if (!needsLogin.value) synchronize(account)
    }

    private suspend fun synchronize(account: DriverSession) {
        syncProgress.value = if (syncProgress.value.owner == account.driverId) syncProgress.value.copy(active = true)
            else SyncProgress(active = true, owner = account.driverId)
        try {
            val refreshedSession = api.refreshSession(account)
            if (refreshedSession != account) { sessions.save(refreshedSession); session.value = refreshedSession }
            val blocked = mutableSetOf<Pair<String, String>>()
            for (entry in dao.queued(account.driverId)) {
                val delivery = entry.deliveryId to entry.assignment
                if (entry.failure != null && entry.failure != "Awaiting dispatcher GPS review") blocked.add(delivery)
                if (delivery in blocked) continue
                val event = driverJson.decodeFromString<TaskEvent>(entry.payload)
                when (val status = api.submit(account, entry.deliveryId, event)) {
                    204 -> acknowledge(entry)
                    202 -> { blocked.add(delivery); dao.fail(entry.sequence, "Awaiting dispatcher GPS review") }
                    400, 404, 409, 413, 422 -> {
                        blocked.add(delivery)
                        val reason = if (status == 422 && event.gpsExceptionReason != null) "Dispatcher rejected GPS exception. Contact dispatch or discard and recapture proof."
                            else if (status == 422) "Location proof rejected. Recapture a recent, accurate fix at the drop-off."
                            else if (status == 400 || status == 413) "Proof or event time rejected"
                            else "Assignment or delivery status changed"
                        dao.fail(entry.sequence, reason)
                    }
                    else -> throw DriverHttpException(status)
                }
            }
            val remote = api.list(account)
            database.withTransaction {
                dao.clearTasks(account.driverId)
                dao.saveTasks(remote.map { SavedTask(account.driverId, it.id, driverJson.encodeToString(it)) })
            }
            message.value = if (blocked.isEmpty()) "Up to date" else "Review the updates that failed to sync."
            syncProgress.value = SyncProgress(active = true, owner = account.driverId, refreshedAt = Instant.now())
        } finally {
            syncProgress.value = syncProgress.value.copy(active = false)
        }
    }

    private suspend fun acknowledge(entry: PendingEvent) = database.withTransaction {
        val current = dao.task(entry.owner, entry.deliveryId)?.let {
            driverJson.decodeFromString<TaskInfo>(it.payload)
        }
        if (current?.assignedAtUtc == entry.assignment) {
            if (entry.kind == "Deliver") dao.removeTask(entry.owner, entry.deliveryId)
            else dao.saveTasks(listOf(SavedTask(entry.owner, entry.deliveryId,
                driverJson.encodeToString(current.copy(status = "PickedUp")))))
        }
        dao.removeEvent(entry.sequence)
    }

    private fun runOperation(action: suspend () -> Unit) {
        viewModelScope.launch {
            if (!operation.tryLock()) return@launch
            busy.value = true
            message.value = null
            try {
                action()
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (error: DriverHttpException) {
                if (error.status == 401 || error.status == 403) {
                    needsLogin.value = session.value != null
                    message.value = if (needsLogin.value) "Sign in again to sync your updates."
                        else "Sign-in failed. Check your driver credentials."
                } else message.value = "Server unavailable. Try again shortly."
            } catch (_: IOException) {
                message.value = if (session.value == null) "Can’t connect. Check your connection and try again."
                    else "Offline. Updates saved on this device."
            } catch (_: Exception) {
                message.value = "Couldn’t complete the action. Try again."
            } finally {
                busy.value = false
                operation.unlock()
            }
        }
    }

    override fun onCleared() {
        api.close()
        database.close()
        super.onCleared()
    }
}
