package com.example.dalanmobile.driver

import com.example.dalanmobile.BuildConfig
import com.example.dalanmobile.dto.StaffLoginRequestDTO
import com.example.dalanmobile.dto.TokenResponseDTO
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.android.Android
import io.ktor.client.plugins.HttpTimeout
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.request.bearerAuth
import io.ktor.client.request.get
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

@Serializable
data class DriverSession(val driverId: String, val token: String, val gpsProofEnabled: Boolean = false)

@Serializable
data class TrackingStart(val sessionId: String, val vehicleId: String)
@Serializable
data class TrackingFix(val sampleId: String, val capturedAtUtc: String, val latitude: Double,
                       val longitude: Double, val accuracyMeters: Double)
@Serializable
data class TrackingResponse(val continueTracking: Boolean)

@Serializable
data class TaskInfo(
    val id: String,
    val recipientName: String,
    val pickupAddress: String,
    val dropoffAddress: String,
    val status: String,
    val assignedAtUtc: String,
    val routeId: String? = null,
    val pickupPosition: Int? = null,
    val dropoffPosition: Int? = null,
    val pickupLatitude: Double? = null,
    val pickupLongitude: Double? = null,
    val dropoffLatitude: Double? = null,
    val dropoffLongitude: Double? = null,
)

@Serializable
data class DriverRoadRoute(
    val distanceMeters: Double,
    val durationSeconds: Double,
    val encodedPolyline6: String,
    val maneuvers: List<DriverManeuver>? = null,
)
@Serializable data class DriverManeuver(val instruction: String, val distanceMeters: Double,
    val durationSeconds: Double, val beginShapeIndex: Int, val endShapeIndex: Int)
@Serializable data class NavigationPoint(val latitude: Double, val longitude: Double)
@Serializable data class DriverNavigationRequest(val origin: NavigationPoint, val capturedAtUtc: String, val accuracyMeters: Double)
@Serializable data class DriverNavigationRoute(val deliveryId: String, val stopKind: String, val address: String,
    val destination: NavigationPoint, val route: DriverRoadRoute)

@Serializable
data class TaskEvent(
    val eventId: String,
    val assignedAtUtc: String,
    val capturedAtUtc: String,
    val kind: String,
    val latitude: Double? = null,
    val longitude: Double? = null,
    val recipientConfirmation: String? = null,
    val photo: String? = null,
    val locationAccuracyMeters: Double? = null,
    val locationCapturedAtUtc: String? = null,
    val gpsExceptionReason: String? = null,
)

@Serializable
private data class GpsReviewSubmission(val proof: TaskEvent, val reason: String)

@Serializable
private data class SessionResponse(val driverId: String, val gpsProofEnabled: Boolean = false)

internal val driverJson = Json { ignoreUnknownKeys = true }

class DriverApi : AutoCloseable {
    private val client = HttpClient(Android) {
        followRedirects = false
        install(ContentNegotiation) { json(driverJson) }
        install(HttpTimeout) {
            requestTimeoutMillis = 20_000
            connectTimeoutMillis = 10_000
            socketTimeoutMillis = 20_000
        }
    }

    suspend fun login(email: String, password: String): DriverSession {
        val login = client.post(url("login")) {
            contentType(ContentType.Application.Json)
            setBody(StaffLoginRequestDTO(email.trim(), password))
        }.requireSuccess().body<TokenResponseDTO>()
        val session = client.get(url("session")) { bearerAuth(login.accessToken) }
            .requireSuccess().body<SessionResponse>()
        return DriverSession(session.driverId, login.accessToken, session.gpsProofEnabled)
    }

    suspend fun refreshSession(session: DriverSession): DriverSession {
        val current = client.get(url("session")) { bearerAuth(session.token) }.requireSuccess().body<SessionResponse>()
        if (current.driverId != session.driverId) throw DriverHttpException(403)
        return session.copy(gpsProofEnabled = current.gpsProofEnabled)
    }

    suspend fun list(session: DriverSession): List<TaskInfo> =
        client.get(url("tasks")) { bearerAuth(session.token) }.requireSuccess().body()

    suspend fun startTracking(session: DriverSession): TrackingStart =
        client.post(url("tracking/start")) { bearerAuth(session.token) }.requireSuccess().body()

    suspend fun track(session: DriverSession, id: String, fix: TrackingFix): TrackingResponse =
        client.post(url("tracking/$id/samples")) {
            bearerAuth(session.token)
            contentType(ContentType.Application.Json)
            setBody(fix)
        }.requireSuccess().body()

    suspend fun stopTracking(session: DriverSession, id: String) {
        client.post(url("tracking/$id/stop")) { bearerAuth(session.token) }.requireSuccess()
    }

    suspend fun route(session: DriverSession, delivery: String): DriverRoadRoute =
        client.get(url("tasks/$delivery/route")) { bearerAuth(session.token) }.requireSuccess().body()

    suspend fun navigate(session: DriverSession, request: DriverNavigationRequest): DriverNavigationRoute? {
        val response = client.post(url("navigation")) {
            bearerAuth(session.token); contentType(ContentType.Application.Json); setBody(request)
        }.requireSuccess()
        return if (response.status.value == 204) null else response.body()
    }

    suspend fun submit(session: DriverSession, delivery: String, event: TaskEvent): Int =
        client.post(url("tasks/$delivery/" + if (event.gpsExceptionReason == null) "events" else "gps-exceptions")) {
            bearerAuth(session.token)
            contentType(ContentType.Application.Json)
            if (event.gpsExceptionReason == null) setBody(event)
            else setBody(GpsReviewSubmission(event.copy(gpsExceptionReason = null), event.gpsExceptionReason))
        }.status.value

    private fun url(path: String) = "${BuildConfig.API_BASE_URL.trimEnd('/')}/api/driver/$path"

    private fun HttpResponse.requireSuccess(): HttpResponse {
        if (!status.isSuccess()) throw DriverHttpException(status.value)
        return this
    }

    override fun close() = client.close()
}

class DriverHttpException(val status: Int) : Exception()
