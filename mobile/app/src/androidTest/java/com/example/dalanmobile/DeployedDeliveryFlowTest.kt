package com.example.dalanmobile

import android.graphics.Bitmap
import android.util.Base64
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.hasClickAction
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
import com.example.dalanmobile.driver.DriverViewModel
import com.example.dalanmobile.driver.SessionStore
import com.example.dalanmobile.driver.TaskInfo
import java.io.ByteArrayOutputStream
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

/** Explicitly targeted deployed test records only. Never clears storage, signs out, or changes real jobs. */
@RunWith(AndroidJUnit4::class)
class DeployedDeliveryFlowTest {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val arguments get() = InstrumentationRegistry.getArguments()

    private fun guard() {
        assumeTrue(arguments.getString("dalanDeliveryE2E") == "true")
        assertEquals("https://dalan.lol/", BuildConfig.API_BASE_URL)
        assertTrue(arguments.getString("dalanExpectedRecipient").orEmpty().startsWith("E2E TEST "))
        val session = SessionStore(instrumentation.targetContext).load()
        assertNotNull("The phone must already be signed in as Driver", session)
        assertEquals(arguments.getString("dalanExpectedDriverId"), session!!.driverId)
    }

    private fun model(): DriverViewModel {
        lateinit var value: DriverViewModel
        instrumentation.runOnMainSync { value = ViewModelProvider(compose.activity)[DriverViewModel::class.java] }
        return value
    }

    private suspend fun task(model: DriverViewModel): TaskInfo {
        val id = arguments.getString("dalanDeliveryId")!!
        model.sync()
        val state = withTimeout(60_000) { model.state.first { !it.busy && !it.needsLogin && it.tasks.any { task -> task.id == id } } }
        val task = state.tasks.single { it.id == id }
        assertEquals(arguments.getString("dalanExpectedRecipient"), task.recipientName)
        return task
    }

    private fun openTask(task: TaskInfo) {
        compose.onNodeWithText("Deliveries", useUnmergedTree = true).performClick()
        val card = hasText(task.recipientName) and hasClickAction()
        compose.onNode(hasScrollAction()).performScrollToNode(card)
        compose.onNode(card).performClick()
    }

    @Test fun target_delivery_route_opens_in_mobile_ui(): Unit = runBlocking {
        guard()
        val task = task(model())
        DriverApi().use { api ->
            val session = SessionStore(instrumentation.targetContext).load()!!
            val route = api.route(session, task.id)
            assertTrue(route.distanceMeters > 0)
            assertTrue(route.durationSeconds > 0)
            assertTrue(route.encodedPolyline6.isNotBlank())
        }
        openTask(task)
        compose.onNodeWithText("View route map").performClick()
        compose.waitUntil(45_000) {
            compose.onAllNodes(hasText("min by road", substring = true)).fetchSemanticsNodes().isNotEmpty()
        }
        compose.onNodeWithText("Back to delivery").performClick()
    }

    @Test fun target_delivery_pickup_uses_the_real_ui(): Unit = runBlocking {
        guard()
        val model = model()
        val task = task(model)
        assertEquals("Assigned", task.status)
        compose.onNodeWithText("Account").performClick()
        compose.onNode(hasScrollAction()).performScrollToNode(hasText("Start sharing location"))
        compose.onNodeWithText("Start sharing location").assertIsEnabled()
        openTask(task)
        compose.onNodeWithText("Confirm pickup").performClick()
        withTimeout(45_000) { model.state.first { !it.busy && it.tasks.any { item -> item.id == task.id && item.status == "PickedUp" }
            && it.events.none { event -> event.deliveryId == task.id } } }
        DriverApi().use { api ->
            assertEquals("PickedUp", api.list(SessionStore(instrumentation.targetContext).load()!!).single { it.id == task.id }.status)
        }
    }

    @Test fun target_delivery_proof_uses_app_queue_with_generated_test_artifacts(): Unit = runBlocking {
        guard()
        val model = model()
        val task = task(model)
        assertEquals("PickedUp", task.status)
        openTask(task)
        compose.onNodeWithText("Proof of delivery").assertExists()
        val bitmap = Bitmap.createBitmap(16, 16, Bitmap.Config.ARGB_8888)
        val bytes = try { ByteArrayOutputStream().use { output ->
            bitmap.eraseColor(android.graphics.Color.LTGRAY)
            assertTrue(bitmap.compress(Bitmap.CompressFormat.JPEG, 80, output))
            output.toByteArray()
        } } finally { bitmap.recycle() }
        // Clearly synthetic proof for this labeled test job; this does not test camera/GPS hardware capture.
        model.record(task, "Deliver", task.dropoffLatitude!!, task.dropoffLongitude!!,
            "E2E TEST - generated image and simulated destination location", Base64.encodeToString(bytes, Base64.NO_WRAP))
        withTimeout(45_000) { model.state.first { !it.busy && it.tasks.none { item -> item.id == task.id }
            && it.events.none { event -> event.deliveryId == task.id } } }
        model.sync()
        withTimeout(30_000) { model.state.first { !it.busy } }
        DriverApi().use { api ->
            assertTrue(api.list(SessionStore(instrumentation.targetContext).load()!!).none { it.id == task.id })
        }
    }
}
