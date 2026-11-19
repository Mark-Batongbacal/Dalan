package com.example.dalanmobile

import android.app.NotificationManager
import android.content.Intent
import androidx.compose.ui.test.hasScrollAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.lifecycle.ViewModelProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.DriverApi
import com.example.dalanmobile.driver.DriverLocationService
import com.example.dalanmobile.driver.DriverViewModel
import com.example.dalanmobile.driver.TaskEvent
import com.example.dalanmobile.driver.driverJson
import java.io.File
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.Serializable
import org.junit.After
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

/** Opt-in real foreground-service check. Install only the isolated E2E application. */
@RunWith(AndroidJUnit4::class)
class DriverTrackingE2ETest {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext

    @After fun stopSharing() { DriverLocationService.stop(context) }

    @Test fun minimized_sharing_notification_stop_and_completion_end_the_session(): Unit = runBlocking {
        assumeTrue(BuildConfig.APPLICATION_ID.endsWith(".e2e"))
        val file = File(context.filesDir, "dalan-e2e/fixture.json")
        assumeTrue(file.exists())
        val fixture = driverJson.decodeFromString<Fixture>(file.readText())
        assertEquals(fixture.apiBaseUrl, BuildConfig.API_BASE_URL)
        lateinit var model: DriverViewModel
        instrumentation.runOnMainSync { model = ViewModelProvider(compose.activity)[DriverViewModel::class.java] }
        model.login(fixture.driverEmail, fixture.password)
        withTimeout(60_000) { model.state.first { it.signedIn && !it.busy && it.tasks.size == fixture.initialTasks } }
        startFromScreen()
        awaitStarted()
        val notifications = context.getSystemService(NotificationManager::class.java)
        assertTrue(notifications.activeNotifications.any { it.id == 204 })
        DriverApi().use { api ->
            val owner = api.login(fixture.driverEmail, fixture.password)
            val first = api.startTracking(owner)
            home()
            delay(35_000)
            assertTrue("Foreground location sharing must survive minimizing", DriverLocationService.state.value.active)
            val notification = notifications.activeNotifications.single { it.id == 204 }.notification
            assertTrue(notification.actions.isNotEmpty())
            notification.actions.first().actionIntent.send()
            withTimeout(15_000) { DriverLocationService.state.first { !it.active && !it.stopping } }
            withTimeout(15_000) {
                while (api.startTracking(owner).sessionId == first.sessionId) delay(500)
            }
            assertFalse(notifications.activeNotifications.any { it.id == 204 })
            context.startActivity(Intent(context, MainActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
            startFromScreen()
            awaitStarted()
            home()
            for (task in api.list(owner)) {
                if (task.status == "Assigned") assertEquals(204, api.submit(owner, task.id,
                    TaskEvent(UUID.randomUUID().toString(), task.assignedAtUtc, Instant.now().toString(), "Pickup")))
                assertEquals(204, api.submit(owner, task.id, TaskEvent(UUID.randomUUID().toString(),
                    task.assignedAtUtc, Instant.now().toString(), "Deliver", 14.6, 121.03, "E2E recipient", "/9j/4P/Z")))
            }
            withTimeout(75_000) { DriverLocationService.state.first { !it.active && !it.stopping } }
            assertTrue(DriverLocationService.state.value.message.orEmpty().contains("finished"))
            withTimeout(10_000) { while (notifications.activeNotifications.any { it.id == 204 }) delay(100) }
        }
        model.logout()
    }

    private fun startFromScreen() {
        compose.onNode(hasScrollAction()).performScrollToNode(hasText("Start sharing location"))
        compose.onNodeWithText("Start sharing location").performClick()
    }
    private suspend fun awaitStarted() = withTimeout(40_000) {
        DriverLocationService.state.first { it.active && it.message.orEmpty().let { text -> text.contains("Waiting") || text.contains("Location shared") } }
    }
    private fun home() { instrumentation.uiAutomation.executeShellCommand("input keyevent KEYCODE_HOME").close() }
    @Serializable private data class Fixture(val apiBaseUrl: String, val driverEmail: String, val password: String, val initialTasks: Int)
}
