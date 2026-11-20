package com.example.dalanmobile

import android.graphics.Bitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.room.Room
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.*
import java.io.File
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

/** Synthetic, offline fixtures in a separately installed application; never uses the driver's live data. */
@RunWith(AndroidJUnit4::class)
class DriverAppearanceTest {
    @get:Rule val compose = createEmptyComposeRule()
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val recipient = "TEST · Alex Santos"

    private fun seed(signedIn: Boolean = true, empty: Boolean = false) = runBlocking {
        org.junit.Assume.assumeTrue(BuildConfig.APPLICATION_ID.endsWith(".dln40.e2e"))
        org.junit.Assume.assumeTrue(BuildConfig.API_BASE_URL == "http://127.0.0.1:9/")
        context.deleteDatabase("driver.db")
        context.getSharedPreferences("appearance", 0).edit().putString("mode", "Light").commit()
        SessionStore(context).clear()
        if (signedIn) {
            SessionStore(context).save(DriverSession("visual-fixture", "synthetic-not-a-token", true))
            val database = Room.databaseBuilder(context, DriverDatabase::class.java, "driver.db").build()
            try {
                val tasks = listOf(
                    TaskInfo("proof", recipient, "TEST · Central warehouse", "TEST · Riverside reception", "PickedUp", "2026-11-20T01:00:00Z", pickupPosition = 1, dropoffPosition = 2, pickupLatitude = 14.64, pickupLongitude = 121.0, dropoffLatitude = 14.65, dropoffLongitude = 121.01),
                    TaskInfo("pickup", "TEST · Long recipient name for a delivery at the university reception", "TEST · Main warehouse loading bay", "TEST · University campus, administration building", "Assigned", "2026-11-20T02:00:00Z"),
                )
                if (!empty) {
                    database.driverDao().saveTasks(tasks.map { SavedTask("visual-fixture", it.id, driverJson.encodeToString(TaskInfo.serializer(), it)) })
                    database.driverDao().enqueue(PendingEvent(owner = "visual-fixture", deliveryId = "pickup", assignment = tasks[1].assignedAtUtc, kind = "Pickup", payload = driverJson.encodeToString(TaskEvent.serializer(), TaskEvent("fixture-event", tasks[1].assignedAtUtc, "2026-11-20T03:00:00Z", "Pickup")), failure = "Assignment or delivery status changed"))
                }
            } finally { database.close() }
        }
    }

    private fun scrollTo(text: String) = compose.onNode(hasScrollAction()).performScrollToNode(hasText(text))
    private fun tap(text: String) { scrollTo(text); compose.onNodeWithText(text).performClick() }
    private fun capture(name: String) {
        compose.waitForIdle()
        val phase = InstrumentationRegistry.getArguments().getString("capturePhase") ?: "after"
        val directory = File(context.getExternalFilesDir(null), "dln40/$phase").apply { mkdirs() }
        // Compose idleness does not include the platform dialog/window transition.
        Thread.sleep(500)
        val bitmap = instrumentation.uiAutomation.takeScreenshot()
        File(directory, "$name.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        bitmap.recycle()
    }

    @Test fun themes_delivery_proof_and_queue_remain_usable() {
        seed()
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            scenario.onActivity { it.window.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) }
            compose.waitUntil(15_000) { compose.onAllNodes(hasText(recipient, substring = true)).fetchSemanticsNodes().isNotEmpty() }
            for (theme in listOf("Light", "Dark", "System")) {
                compose.onNodeWithText("Account").performClick()
                tap(theme)
                capture("account-$theme")
                compose.onNodeWithText("Home").performClick()
                capture("home-$theme")
                compose.onNodeWithText("Deliveries", useUnmergedTree = true).performClick()
                capture("deliveries-$theme")
                val card = hasText(recipient) and hasClickAction()
                compose.onNode(hasScrollAction()).performScrollToNode(card)
                compose.onNode(card).performClick()
                capture("details-$theme")
                scrollTo("Confirm delivery")
                compose.onNodeWithText("Confirm delivery").assertIsNotEnabled()
                capture("proof-$theme")
                tap("View route map")
                compose.waitUntil(30_000) { compose.onAllNodes(hasText("Road route unavailable", substring = true)).fetchSemanticsNodes().isNotEmpty() }
                capture("route-$theme")
                compose.onNodeWithText("Retry route").performScrollTo().assertIsEnabled().performClick()
                compose.waitUntil(30_000) { compose.onAllNodes(hasText("Road route unavailable", substring = true)).fetchSemanticsNodes().isNotEmpty() }
                capture("route-recovery-$theme")
                compose.onNodeWithText("Back to delivery").performScrollTo().performClick()
                compose.waitForIdle()
                // Back returns to the list without dropping queued events or leaving the app.
                scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
                compose.onNodeWithText("Assigned deliveries").assertExists()
            }
            scenario.recreate()
            compose.onNodeWithText("Account").performClick()
            scrollTo("System")
            compose.onNodeWithText("System").assertIsSelected()

        }
        val database = Room.databaseBuilder(context, DriverDatabase::class.java, "driver.db").build()
        try { runBlocking { assertEquals(1, database.driverDao().queued("visual-fixture").size) } }
        finally { database.close() }
    }

    @Test fun sign_in_themes_keep_validation_and_recovery() {
        seed(false)
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            scenario.onActivity { it.window.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) }
            for (theme in listOf("Light", "Dark", "System")) {
                tap(theme)
                scrollTo("Sign in to workspace")
                compose.onNodeWithText("Sign in to workspace").assertIsNotEnabled()
                capture("login-$theme")
                scrollTo("Forgot password?")
                compose.onNodeWithText("Forgot password?").assertExists()
            }
        }
    }

    @Test fun empty_routes_offer_sync_instead_of_invented_work() {
        seed(empty = true)
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            scenario.onActivity { it.window.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) }
            compose.waitUntil(15_000) { compose.onAllNodes(hasText("Ready when you are")).fetchSemanticsNodes().isNotEmpty() }
            compose.onNodeWithText("Route").performClick()
            compose.onNodeWithText("No deliveries saved").assertExists()
            compose.onNodeWithText("Sync now").assertExists()
            capture("empty-route")
        }
    }
}
