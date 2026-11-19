package com.example.dalanmobile.driver

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.os.IBinder
import android.os.Looper
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.example.dalanmobile.MainActivity
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

data class TrackingUiState(val driverId: String? = null, val active: Boolean = false, val message: String? = null,
                           val stopping: Boolean = false)

/** Explicit foreground sharing only. No persisted GPS queue, boot receiver, or automatic restart. */
class DriverLocationService : Service() {
    companion object {
        private const val CHANNEL = "active-delivery-location"
        private const val NOTIFICATION = 204
        private const val STOP = "com.example.dalanmobile.STOP_TRACKING"
        private val mutableState = MutableStateFlow(TrackingUiState())
        val state = mutableState.asStateFlow()

        fun start(context: Context) {
            check(!state.value.stopping) { "Previous sharing session is stopping" }
            ContextCompat.startForegroundService(context, Intent(context, DriverLocationService::class.java))
        }
        fun stop(context: Context) { context.stopService(Intent(context, DriverLocationService::class.java)) }
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var job: Job? = null
    @Volatile private var finished = false
    private lateinit var manager: LocationManager
    @Volatile private var latest: Location? = null
    private val listener = LocationListener { point -> latest = Location(point) }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        manager = getSystemService(LocationManager::class.java)
        getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel(CHANNEL, "Active delivery location", NotificationManager.IMPORTANCE_LOW))
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == STOP) { stopSelf(); return START_NOT_STICKY }
        if (job != null) return START_NOT_STICKY
        val owner = SessionStore(this).load()
        if (owner == null) { stopSelf(); return START_NOT_STICKY }
        mutableState.value = TrackingUiState(owner.driverId, true, "Starting location sharing…")
        try {
            val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
            val stop = PendingIntent.getService(this, 1, Intent(this, DriverLocationService::class.java).setAction(STOP), PendingIntent.FLAG_IMMUTABLE)
            val notification = NotificationCompat.Builder(this, CHANNEL)
                .setSmallIcon(android.R.drawable.ic_menu_mylocation).setContentTitle("Dalan location sharing")
                .setContentText("Active deliveries · tap Stop to end sharing")
                .setContentIntent(open).setOngoing(true).setCategory(NotificationCompat.CATEGORY_SERVICE)
                .addAction(0, "Stop sharing", stop).build()
            ServiceCompat.startForeground(this, NOTIFICATION, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION)
        } catch (_: Exception) {
            mutableState.value = TrackingUiState(owner.driverId, false, "Couldn’t start sharing. Check location permissions.")
            stopSelf()
            return START_NOT_STICKY
        }
        job = scope.launch {
            val api = DriverApi()
            var remote: TrackingStart? = null
            var message = "Location sharing stopped."
            try {
                remote = api.startTracking(owner)
                val providers = listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER)
                val fine = ContextCompat.checkSelfPermission(this@DriverLocationService, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
                var registered = false
                for (provider in providers) {
                    if (provider == LocationManager.GPS_PROVIDER && !fine) continue
                    if (provider !in manager.allProviders) continue
                    manager.requestLocationUpdates(provider, 15_000L, 10f, listener, Looper.getMainLooper())
                    registered = true
                }
                check(registered) { "No location provider" }
                var lastSentTime = 0L
                var lastVerified = System.currentTimeMillis()
                while (isActive) {
                    val current = SessionStore(this@DriverLocationService).load()
                    if (current != owner) break
                    try {
                        if (api.list(owner).none { it.status == "Assigned" || it.status == "PickedUp" }) {
                            message = "All active deliveries finished. Sharing stopped."
                            break
                        }
                        lastVerified = System.currentTimeMillis()
                        val point = latest
                        if (point != null && point.time > lastSentTime && point.hasAccuracy() && point.accuracy <= 100 &&
                            System.currentTimeMillis() - point.time in 0..120_000) {
                            val accepted = api.track(owner, remote.sessionId, TrackingFix(UUID.randomUUID().toString(),
                                Instant.ofEpochMilli(point.time).toString(), point.latitude, point.longitude, point.accuracy.toDouble()))
                            if (!accepted.continueTracking) { message = "Delivery session ended. Sharing stopped."; break }
                            lastSentTime = point.time
                            mutableState.value = TrackingUiState(owner.driverId, true, "Location shared. Continues while minimized.")
                        } else mutableState.value = TrackingUiState(owner.driverId, true, "Waiting for an accurate location fix…")
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (error: DriverHttpException) {
                        if (error.status in listOf(401, 403, 404, 409, 503)) {
                            message = "Sharing stopped. Sign in or check your active vehicle assignment."
                            break
                        }
                        mutableState.value = TrackingUiState(owner.driverId, true, "Location not sent. Retrying; no GPS history is queued.")
                    } catch (_: java.io.IOException) {
                        mutableState.value = TrackingUiState(owner.driverId, true, "Offline. No GPS history is queued.")
                    }
                    if (System.currentTimeMillis() - lastVerified > 300_000) {
                        message = "Couldn’t verify active deliveries for five minutes. Sharing stopped."
                        break
                    }
                    delay(30_000)
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { message = "Sharing unavailable. Check permissions and active deliveries, then try again." }
            finally {
                manager.removeUpdates(listener)
                latest = null
                mutableState.value = TrackingUiState(owner.driverId, false, "Stopping location sharing…", stopping = true)
                withContext(NonCancellable) { withTimeoutOrNull(5_000) {
                    try { remote?.let { api.stopTracking(owner, it.sessionId) } } catch (_: Exception) { }
                } }
                api.close()
                finished = true
                mutableState.value = TrackingUiState(owner.driverId, false, message)
                stopSelf()
            }
        }
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        manager.removeUpdates(listener)
        latest = null
        if (!finished && job != null) mutableState.value = mutableState.value.copy(active = false, stopping = true)
        scope.cancel()
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        super.onDestroy()
    }
}
