package com.example.dalanmobile

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.Density
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.*
import com.example.dalanmobile.ui.theme.DalanMobileTheme
import java.io.File
import java.time.Instant
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class DriverFeedbackUiTest {
    @get:Rule val compose = createComposeRule()
    private val rejected = PendingEvent(owner = "fixture-driver", deliveryId = "fixture-delivery", assignment = "fixture",
        kind = "Pickup", payload = "", failure = "Assignment or delivery status changed")

    @Test fun navigation_legend_keeps_pickup_and_dropoff_visible_with_current_position() {
        val navigating = mutableStateOf(false)
        compose.setContent { DalanMobileTheme(true) { DriverRouteLegend(navigating.value) } }
        compose.onNodeWithText("Pickup").assertExists()
        compose.onNodeWithText("Drop-off").assertExists()
        compose.runOnIdle { navigating.value = true }
        compose.onNodeWithText("Current position").assertExists()
        compose.onNodeWithText("Pickup").assertExists()
        compose.onNodeWithText("Drop-off").assertExists()
    }

    @Test fun rejected_assignment_requires_explicit_discard_confirmation() {
        var discarded = 0
        compose.setContent { DalanMobileTheme(false) { FailedUpdateCard(rejected, "Test recipient", 2, false) { discarded++ } } }
        compose.onNodeWithText("Assignment changed").assertExists()
        compose.onNodeWithText("Review discard options").performClick()
        compose.onNodeWithText("Keep updates").performClick()
        compose.runOnIdle { assertEquals(0, discarded) }
        compose.onNodeWithText("Review discard options").performClick()
        compose.onNodeWithText("Discard saved updates").performClick()
        compose.runOnIdle { assertEquals(1, discarded) }
    }

    @Test fun busy_queue_does_not_allow_discard() {
        compose.setContent { DalanMobileTheme(true) { FailedUpdateCard(rejected, "Test recipient", 2, true) { fail("Busy discard") } } }
        compose.onNodeWithText("Review discard options").assertIsNotEnabled()
    }

    @Test fun sync_states_do_not_confuse_local_queue_with_server_acceptance() {
        val state = mutableStateOf(DriverWorkspace(signedIn = true, driverId = "fixture-driver", events = listOf(rejected.copy(failure = null))))
        compose.setContent { DalanMobileTheme(false) { SyncStatusCard(state.value) } }
        compose.onNodeWithText("Updates queued on this device").assertExists()
        compose.runOnIdle { state.value = state.value.copy(syncing = true, busy = true) }
        compose.onNodeWithText("Syncing with server…").assertExists()
        compose.runOnIdle { state.value = state.value.copy(syncing = false, busy = false, events = listOf(rejected)) }
        compose.onNodeWithText("Updates need review").assertExists()
        compose.runOnIdle { state.value = state.value.copy(events = emptyList(), message = "Up to date", lastSyncedAt = Instant.parse("2026-11-20T00:00:00Z")) }
        compose.onNodeWithText("Synchronized").assertExists()
        compose.runOnIdle { state.value = state.value.copy(message = "Offline. Updates stay on this device.") }
        compose.onNodeWithText("Synchronized").assertDoesNotExist()
        compose.runOnIdle { state.value = state.value.copy(needsLogin = true) }
        compose.onNodeWithText("Sign-in required").assertExists()
    }

    @Test fun feedback_remains_scrollable_in_large_text_and_each_appearance() {
        val theme = mutableStateOf<Boolean?>(false)
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, 1.6f)) {
                DalanMobileTheme(theme.value ?: androidx.compose.foundation.isSystemInDarkTheme()) {
                    Column(Modifier.verticalScroll(rememberScrollState())) {
                        SyncStatusCard(DriverWorkspace(signedIn = true, events = listOf(rejected)))
                        FailedUpdateCard(rejected, "Test recipient with a longer delivery label", 2, false) { }
                    }
                }
            }
        }
        for ((name, dark) in listOf("light" to false, "dark" to true, "system" to null)) {
            compose.runOnIdle { theme.value = dark }
            compose.onNodeWithText("Review discard options").performScrollTo().assertIsDisplayed()
            val file = File(InstrumentationRegistry.getInstrumentation().targetContext.getExternalFilesDir(null), "dln28-feedback-$name.png")
            file.outputStream().use { compose.onRoot().captureToImage().asAndroidBitmap().compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it) }
        }
    }
}
