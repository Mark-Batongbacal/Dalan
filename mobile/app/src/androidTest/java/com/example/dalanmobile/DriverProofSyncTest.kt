package com.example.dalanmobile

import android.app.Application
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.ViewModelStore
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.*
import java.net.InetAddress
import java.net.ServerSocket
import java.time.Instant
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.Rule
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import com.example.dalanmobile.ui.theme.DalanMobileTheme
import org.junit.runner.RunWith

/** Isolated APK only. Exercises the real HTTP client, ViewModel and Room against a local 422 response. */
@RunWith(AndroidJUnit4::class)
class DriverProofSyncTest {
    @get:Rule val compose = createComposeRule()
    @Test fun gps_rejection_survives_restart_and_is_not_retried_as_offline(): Unit = runBlocking {
        assumeTrue(BuildConfig.APPLICATION_ID.endsWith(".e2e") && BuildConfig.API_BASE_URL == "http://127.0.0.1:5072/")
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val submissions = AtomicInteger()
        val reviewSubmissions = AtomicInteger()
        val approved = java.util.concurrent.atomic.AtomicBoolean(false)
        val task = TaskInfo("proof-task", "Test recipient", "Warehouse", "Destination", "PickedUp", "2026-11-20T00:00:00Z")
        val listener = ServerSocket(5072, 10, InetAddress.getByName("127.0.0.1"))
        val server = thread(isDaemon = true) {
            while (!listener.isClosed) {
                val socket = try { listener.accept() } catch (_: java.io.IOException) { break }
                socket.use {
                    it.soTimeout = 10000
                    val reader = it.getInputStream().bufferedReader()
                    val request = reader.readLine().orEmpty()
                    var length = 0
                    while (true) {
                        val header = reader.readLine().orEmpty()
                        if (header.isEmpty()) break
                        if (header.startsWith("Content-Length:", true)) length = header.substringAfter(':').trim().toInt()
                    }
                    val body = CharArray(length)
                    var read = 0
                    while (read < length) {
                        val count = reader.read(body, read, length - read)
                        if (count < 0) break
                        read += count
                    }
                    val rejected = request.contains("/events ")
                    val review = request.contains("/gps-exceptions ")
                    if (rejected) submissions.incrementAndGet()
                    if (review) reviewSubmissions.incrementAndGet()
                    val response = when {
                        rejected || review -> if (review && approved.get()) "" else "{}"
                        request.contains("/login ") -> """{"accessToken":"fixture-token","tokenType":"Bearer","expiresIn":3600}"""
                        request.contains("/session ") -> """{"driverId":"proof-driver","gpsProofEnabled":true}"""
                        else -> if (approved.get()) "[]" else driverJson.encodeToString(listOf(task))
                    }.encodeToByteArray()
                    val status = if (rejected) "422 Unprocessable Entity" else if (review) if (approved.get()) "204 No Content" else "202 Accepted" else "200 OK"
                    it.getOutputStream().write("HTTP/1.1 $status\r\nContent-Type: application/json\r\nContent-Length: ${response.size}\r\nConnection: close\r\n\r\n".encodeToByteArray())
                    it.getOutputStream().write(response)
                }
            }
        }
        var store: ViewModelStore? = null
        fun model(): DriverViewModel {
            lateinit var result: DriverViewModel
            instrumentation.runOnMainSync {
                store = ViewModelStore()
                result = ViewModelProvider(store!!, ViewModelProvider.AndroidViewModelFactory(context.applicationContext as Application))[DriverViewModel::class.java]
            }
            return result
        }
        try {
            SessionStore(context).clear()
            context.deleteDatabase("driver.db")
            var driver = model()
            driver.login("fixture@dalan.test", "Fixture-only!2026")
            withTimeout(20000) { driver.state.first { it.signedIn && !it.busy && it.tasks.size == 1 } }
            val fix = Instant.now().minusSeconds(10).toString()
            driver.record(task, "Deliver", 14.0, 121.0, "Recipient", "jpeg", 8.5, fix)
            val rejected = withTimeout(20000) { driver.state.first { !it.busy && !it.syncing && it.events.singleOrNull()?.failure != null } }
            assertTrue(rejected.events.single().failure!!.startsWith("Location proof rejected"))
            assertFalse(rejected.message.orEmpty().contains("unavailable"))
            val saved = driverJson.decodeFromString<TaskEvent>(rejected.events.single().payload)
            assertEquals(8.5, saved.locationAccuracyMeters!!, 0.0)
            assertEquals(fix, saved.locationCapturedAtUtc)
            instrumentation.runOnMainSync { store?.clear() }
            driver = model()
            withTimeout(20000) { driver.state.first { it.events.singleOrNull()?.failure != null } }
            driver.sync()
            withTimeout(20000) { driver.state.first { !it.busy && !it.syncing && it.lastSyncedAt != null } }
            assertEquals(1, submissions.get())
            assertTrue(driver.state.value.gpsProofEnabled)
            val activeDriver = driver
            compose.setContent { DalanMobileTheme(false) { DriverScreen(activeDriver,"Light") {} } }
            compose.onNode(hasScrollAction()).performScrollToNode(hasText("Reason for GPS exception"))
            compose.onNodeWithText("Reason for GPS exception").performScrollTo().performTextInput("GPS blocked by the building; please review photo")
            compose.onNodeWithText("Request dispatcher review").performScrollTo().performClick()
            withTimeout(20000) { driver.state.first { !it.busy && !it.syncing && it.events.singleOrNull()?.failure == "Awaiting dispatcher GPS review" } }
            assertEquals(1,reviewSubmissions.get())
            approved.set(true)
            driver.sync()
            withTimeout(20000) { driver.state.first { !it.busy && !it.syncing && it.events.isEmpty() && it.tasks.isEmpty() } }
            assertEquals(2,reviewSubmissions.get())
        } finally {
            instrumentation.runOnMainSync { store?.clear() }
            SessionStore(context).clear()
            context.deleteDatabase("driver.db")
            listener.close()
            server.join(1000)
        }
    }
}
