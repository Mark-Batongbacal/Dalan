package com.example.dalanmobile

import android.app.Application
import android.graphics.Bitmap
import android.util.Base64
import android.view.View
import android.view.ViewGroup
import android.view.inspector.WindowInspector
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.performScrollToIndex
import androidx.compose.ui.test.hasScrollAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.printToString
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.ViewModelStore
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.DriverApi
import com.example.dalanmobile.driver.DriverHttpException
import com.example.dalanmobile.driver.DriverScreen
import com.example.dalanmobile.driver.DriverViewModel
import com.example.dalanmobile.driver.DriverWorkspace
import com.example.dalanmobile.driver.TaskEvent
import com.example.dalanmobile.driver.TaskInfo
import com.example.dalanmobile.driver.driverJson
import com.example.dalanmobile.ui.theme.DalanMobileTheme
import java.io.ByteArrayOutputStream
import java.io.File
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.Serializable
import org.junit.After
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.maplibre.android.maps.MapView
import org.maplibre.android.style.sources.GeoJsonSource

/** Run phases explicitly against a disposable backend; never uses the normal app's storage. */
@RunWith(AndroidJUnit4::class)
class DriverMultiRouteE2ETest {
    @get:Rule val compose = createComposeRule()
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private var store: ViewModelStore? = null
    private lateinit var model: DriverViewModel
    private lateinit var fixture: Fixture
    private val appearance = mutableStateOf("Light")

    @Before fun setUp() {
        assumeTrue("Use the isolated -PdalanE2E=true APK and a disposable fixture.",
            BuildConfig.APPLICATION_ID.endsWith(".e2e"))
        val file = File(context.filesDir, "dalan-e2e/fixture.json")
        assumeTrue("Provision a disposable E2E fixture first.", file.exists())
        fixture = driverJson.decodeFromString(file.readText())
        assertEquals("Fixture and APK must target the same disposable backend.", fixture.apiBaseUrl, BuildConfig.API_BASE_URL)
        instrumentation.runOnMainSync {
            val owner = ViewModelStore().also { store = it }
            model = ViewModelProvider(owner, ViewModelProvider.AndroidViewModelFactory(
                context.applicationContext as Application))[DriverViewModel::class.java]
        }
    }

    @After fun tearDown() {
        instrumentation.runOnMainSync { store?.clear() }
    }

    @Test fun online_multiple_routes_and_maps_are_scoped_to_each_driver(): Unit = runBlocking {
        model.login(fixture.driverEmail, fixture.password)
        val first = await { it.signedIn && !it.busy && it.tasks.size == fixture.initialTasks }
        assertEquals(2, first.tasks.map { it.routeId }.distinct().size)
        assertTrue(first.tasks.all { it.pickupPosition != null && it.dropoffPosition!! > it.pickupPosition!! })
        showScreen()
        checkMaps(first.tasks)
        compose.onNodeWithText("Home",useUnmergedTree=true).performClick()
        compose.onNodeWithText("Open next delivery").performScrollTo().assertIsDisplayed()
        captureThemes("assigned-deliveries")
        compose.onNodeWithText("Account",useUnmergedTree=true).performClick()
        compose.onNodeWithText("Account & device").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Deliveries",useUnmergedTree=true).performClick()
        captureThemes("delivery-list")
        val firstIds = first.tasks.map { it.id }.toSet()

        DriverApi().use { api ->
            val second = api.login(fixture.otherDriverEmail, fixture.password)
            val other = api.list(second)
            assertEquals(fixture.initialTasks, other.size)
            assertEquals(2, other.map { it.routeId }.distinct().size)
            assertTrue(other.none { it.id in firstIds })
            try {
                api.route(second, first.tasks.first().id)
                fail("A driver must not load another driver's route")
            } catch (error: DriverHttpException) { assertEquals(404, error.status) }
        }
        model.logout(); await { !it.signedIn && !it.busy }
        model.login(fixture.otherDriverEmail, fixture.password)
        val other = await { !it.busy && it.tasks.size == fixture.initialTasks && it.tasks.none { task -> task.id in firstIds } }
        checkMaps(other.tasks)
        model.logout(); await { !it.signedIn && !it.busy }
        model.login(fixture.driverEmail, fixture.password)
        await { !it.busy && it.tasks.size == fixture.initialTasks && it.tasks.all { task -> task.id in firstIds } }
    }

    @Test fun navigation_current_position_follows_next_stop_and_returns_visual_maneuvers(): Unit = runBlocking {
        model.login(fixture.driverEmail, fixture.password)
        val state = await { it.signedIn && !it.busy && it.tasks.size == fixture.initialTasks }
        DriverApi().use { api ->
            val account = api.login(fixture.driverEmail, fixture.password)
            val request = com.example.dalanmobile.driver.DriverNavigationRequest(
                com.example.dalanmobile.driver.NavigationPoint(14.599,121.029), java.time.Instant.now().toString(), 8.0)
            val result = api.navigate(account, request)!!
            assertTrue(state.tasks.any { it.id == result.deliveryId })
            assertEquals("Pickup", result.stopKind)
            assertEquals(14.6, result.destination.latitude, 0.00001)
            assertEquals(121.03, result.destination.longitude, 0.00001)
            assertTrue(result.route.distanceMeters >= 0)
            assertFalse(result.route.maneuvers.isNullOrEmpty())
            assertTrue(result.route.maneuvers!!.all { it.instruction.isNotBlank() && it.endShapeIndex >= it.beginShapeIndex })
            try { api.navigate(account, request.copy(capturedAtUtc=java.time.Instant.now().minusSeconds(300).toString())); fail("Stale origin accepted") }
            catch (expected: DriverHttpException) { assertEquals(422,expected.status) }
        }
    }

    @Test fun offline_queue_spans_two_routes_and_contains_real_jpeg_proof(): Unit = runBlocking {
        val state = await { it.signedIn && it.tasks.size == fixture.initialTasks }
        val differentRoutes = state.tasks.groupBy { it.routeId }.values.map { it.first() }
        assertEquals(2, differentRoutes.size)
        showScreen()
        var count = 0
        for (task in differentRoutes) {
            openTask(task)
            compose.onNodeWithText("Confirm pickup").performScrollTo().performClick()
            count++
            await { !it.busy && it.events.size == count }
            compose.onNodeWithText("Confirm delivery").performScrollTo().assertIsNotEnabled()
            model.record(task, "Deliver", task.dropoffLatitude, task.dropoffLongitude,
                "Synthetic E2E recipient", jpeg())
            count++
            await { !it.busy && it.events.size == count }
            compose.onNodeWithText("Back to deliveries").performScrollTo().performClick()
        }
        val third = state.tasks.first { it.id !in differentRoutes.map { task -> task.id } }
        model.record(third, "Pickup")
        val queued = await { !it.busy && it.events.size == 5 }
        assertTrue(queued.message.orEmpty().contains("Offline"))
        assertFalse(queued.syncing)
        assertNull(queued.lastSyncedAt) // This ViewModel has not refreshed from the stopped server.
        assertEquals(listOf("Pickup", "Deliver", "Pickup", "Deliver", "Pickup"), queued.events.map { it.kind })
        captureThemes("offline-queue")
        File(context.filesDir, "dalan-e2e/events.json").writeText(driverJson.encodeToString(
            queued.events.map { Replay(it.deliveryId, driverJson.decodeFromString<TaskEvent>(it.payload)) }))
    }

    @Test fun offline_queue_and_cached_routes_survive_process_restart(): Unit = runBlocking {
        val cached = await { it.signedIn && it.tasks.size == fixture.initialTasks && it.events.size == 5 }
        assertEquals(2, cached.tasks.map { it.routeId }.distinct().size)
        model.sync()
        await { !it.busy && it.message.orEmpty().contains("Offline") }
        assertEquals(5, model.state.value.events.size)
        showScreen()
        val task = cached.tasks.first()
        openTask(task)
        compose.onNodeWithText("View route map").performScrollTo().performClick()
        compose.waitUntil(30_000) {
            compose.onAllNodes(hasText("Road route unavailable.", substring = true)).fetchSemanticsNodes().isNotEmpty()
        }
        compose.onNodeWithText("Retry map").assertExists()
        compose.onNodeWithText("Back to delivery").performScrollTo().performClick()
    }

    @Test fun reconnect_replays_once_and_switching_driver_hides_other_tasks(): Unit = runBlocking {
        await { it.signedIn && it.events.size == 5 }
        model.sync()
        val synced = await { !it.busy && !it.syncing && it.lastSyncedAt != null && it.events.isEmpty() && it.tasks.size == fixture.initialTasks - 2 }
        assertFalse(synced.syncing)
        assertNotNull(synced.lastSyncedAt)
        assertEquals(1, synced.tasks.count { it.status == "PickedUp" })
        val replay = driverJson.decodeFromString<List<Replay>>(File(context.filesDir, "dalan-e2e/events.json").readText())
        DriverApi().use { api ->
            val session = api.login(fixture.driverEmail, fixture.password)
            for (entry in replay) assertEquals(204, api.submit(session, entry.deliveryId, entry.event))
            val first = replay.first()
            assertEquals(409, api.submit(session, first.deliveryId,
                first.event.copy(capturedAtUtc = "2000-01-01T00:00:00Z")))
        }
        val ownIds = synced.tasks.map { it.id }.toSet()
        model.logout(); await { !it.signedIn && !it.busy }
        model.login(fixture.otherDriverEmail, fixture.password)
        val other = await { !it.busy && it.tasks.size == fixture.initialTasks }
        assertTrue(other.events.isEmpty())
        assertTrue(other.tasks.none { it.id in ownIds })
        assertEquals(2, other.tasks.map { it.routeId }.distinct().size)
        showScreen()
        checkMaps(other.tasks)
        model.logout(); await { !it.signedIn && !it.busy }
    }

    private fun showScreen() = compose.setContent {
        val view = androidx.compose.ui.platform.LocalView.current
        androidx.compose.runtime.DisposableEffect(view) {
            val previous = view.keepScreenOn
            view.keepScreenOn = true
            onDispose { view.keepScreenOn = previous }
        }
        DalanMobileTheme(appearance.value == "Dark") { DriverScreen(model, appearance.value) { appearance.value = it } }
    }

    private fun captureThemes(stage: String) {
        val folder = File(context.filesDir,"dalan-e2e/screenshots").apply { mkdirs() }
        for (mode in listOf("Light","Dark","System")) {
            compose.onNode(hasScrollAction()).performScrollToIndex(0)
            compose.onNodeWithText(mode).performScrollTo().performClick()
            compose.waitForIdle()
            val bitmap = compose.onRoot().captureToImage().asAndroidBitmap()
            File(folder,"$stage-$mode.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG,100,it) }
        }
    }

    private fun checkMaps(tasks: List<TaskInfo>) {
        for (task in tasks.groupBy { it.routeId }.values.map { it.first() }) {
            openTask(task)
            val detailsFolder = File(context.filesDir,"dalan-e2e/screenshots").apply { mkdirs() }
            val detailsImage = instrumentation.uiAutomation.takeScreenshot()
            File(detailsFolder,"delivery-${task.routeId}.png").outputStream().use { detailsImage.compress(Bitmap.CompressFormat.PNG,100,it) }
            detailsImage.recycle()
            compose.onNodeWithText("View route map").performScrollTo().performClick()
            try {
                compose.waitUntil(40_000) {
                    compose.onAllNodes(hasText("min by road", substring = true)).fetchSemanticsNodes().isNotEmpty()
                }
            } catch (error: AssertionError) {
                throw AssertionError("Route view did not show road estimates: " + compose.onRoot().printToString(), error)
            }
            compose.waitUntil(40_000) { routeGeometryLoaded() }
            assertTrue("The online basemap should load, rather than its fallback style.",
                compose.onAllNodes(hasText("Basemap unavailable.", substring = true)).fetchSemanticsNodes().isEmpty())
            val folder = File(context.filesDir, "dalan-e2e/screenshots").apply { mkdirs() }
            val screenshot = instrumentation.uiAutomation.takeScreenshot()
            File(folder, "route-${task.routeId}.png").outputStream().use { screenshot.compress(Bitmap.CompressFormat.PNG, 100, it) }
            screenshot.recycle()
            compose.onNodeWithText("Back to delivery").performScrollTo().performClick()
            compose.onNodeWithText("Back to deliveries").performScrollTo().performClick()
        }
    }

    private fun openTask(task: TaskInfo) {
        compose.onNode(hasScrollAction()).performScrollToNode(hasText(task.recipientName) and hasClickAction())
        compose.onNode(hasText(task.recipientName) and hasClickAction()).performClick()
    }

    private fun routeGeometryLoaded(): Boolean {
        var loaded = false
        instrumentation.runOnMainSync {
            fun visit(view: View) {
                if (view is MapView) view.getMapAsync { map ->
                    val style = map.style
                    loaded = loaded || (style?.isFullyLoaded == true
                        && style.getLayer("dalan-route-line") != null
                        && style.getSourceAs<GeoJsonSource>("dalan-route")?.querySourceFeatures(null)?.isNotEmpty() == true
                        && map.queryRenderedFeatures(android.graphics.RectF(0f, 0f, view.width.toFloat(), view.height.toFloat()),
                            *style.layers.map { it.id }.filterNot { it.startsWith("dalan-") }.toTypedArray()).isNotEmpty())
                }
                if (view is ViewGroup) for (index in 0 until view.childCount) visit(view.getChildAt(index))
            }
            WindowInspector.getGlobalWindowViews().forEach(::visit)
        }
        return loaded
    }

    private suspend fun await(predicate: (DriverWorkspace) -> Boolean): DriverWorkspace =
        withTimeout(60_000) { model.state.first(predicate) }

    private fun jpeg(): String = Bitmap.createBitmap(8, 8, Bitmap.Config.ARGB_8888).let { bitmap ->
        try {
            ByteArrayOutputStream().use { bytes ->
                bitmap.eraseColor(android.graphics.Color.LTGRAY)
                assertTrue(bitmap.compress(Bitmap.CompressFormat.JPEG, 75, bytes))
                Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP)
            }
        } finally { bitmap.recycle() }
    }

    @Serializable private data class Fixture(val apiBaseUrl: String, val driverEmail: String,
        val otherDriverEmail: String, val password: String, val initialTasks: Int)
    @Serializable private data class Replay(val deliveryId: String, val event: TaskEvent)
}
