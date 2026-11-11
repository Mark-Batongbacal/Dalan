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
data class DriverSession(val driverId: String, val token: String)

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
)

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
)

@Serializable
private data class SessionResponse(val driverId: String)

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
        return DriverSession(session.driverId, login.accessToken)
    }

    suspend fun list(session: DriverSession): List<TaskInfo> =
        client.get(url("tasks")) { bearerAuth(session.token) }.requireSuccess().body()

    suspend fun route(session: DriverSession, delivery: String): DriverRoadRoute =
        client.get(url("tasks/$delivery/route")) { bearerAuth(session.token) }.requireSuccess().body()

    suspend fun submit(session: DriverSession, delivery: String, event: TaskEvent): Int =
        client.post(url("tasks/$delivery/events")) {
            bearerAuth(session.token)
            contentType(ContentType.Application.Json)
            setBody(event)
        }.status.value

    private fun url(path: String) = "${BuildConfig.API_BASE_URL.trimEnd('/')}/api/driver/$path"

    private fun HttpResponse.requireSuccess(): HttpResponse {
        if (!status.isSuccess()) throw DriverHttpException(status.value)
        return this
    }

    override fun close() = client.close()
}

class DriverHttpException(val status: Int) : Exception()
