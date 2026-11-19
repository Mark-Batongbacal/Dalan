package com.example.dalanmobile

import android.app.Application
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.ViewModelStore
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.DriverScreen
import com.example.dalanmobile.driver.DriverViewModel
import com.example.dalanmobile.driver.SessionStore
import com.example.dalanmobile.ui.theme.DalanMobileTheme
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class DriverAccessibilityTest {
    @get:Rule val compose = createComposeRule()
    @OptIn(androidx.compose.ui.test.ExperimentalTestApi::class)
    @Test fun actual_driver_login_remains_labelled_and_scrollable_across_themes_and_large_text() {
        assumeTrue(BuildConfig.APPLICATION_ID.endsWith(".e2e"))
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        SessionStore(context).clear()
        val store = ViewModelStore()
        lateinit var model: DriverViewModel
        instrumentation.runOnMainSync { model = ViewModelProvider(store, ViewModelProvider.AndroidViewModelFactory(context.applicationContext as Application))[DriverViewModel::class.java] }
        val appearance = mutableStateOf("Light"); val fontScale = mutableStateOf(1.0f)
        val size = mutableStateOf(DpSize(360.dp,800.dp))
        try {
            compose.setContent {
                val view = androidx.compose.ui.platform.LocalView.current
                androidx.compose.runtime.DisposableEffect(view) {
                    val previous = view.keepScreenOn; view.keepScreenOn = true
                    onDispose { view.keepScreenOn = previous }
                }
                DeviceConfigurationOverride(DeviceConfigurationOverride.ForcedSize(size.value)) {
                    CompositionLocalProvider(LocalDensity provides Density(LocalDensity.current.density,fontScale.value)) {
                        DalanMobileTheme(appearance.value == "Dark") { DriverScreen(model, appearance.value) { appearance.value=it } }
                    }
                }
            }
            for (theme in listOf("Light","Dark","System")) for (scale in listOf(1.0f,2.0f)) for (dimensions in listOf(DpSize(360.dp,800.dp),DpSize(800.dp,360.dp),DpSize(800.dp,900.dp))) {
                compose.runOnIdle { appearance.value=theme; fontScale.value=scale; size.value=dimensions }
                compose.onNodeWithText("Email address").performScrollTo().assertIsDisplayed()
                compose.onNodeWithText("Password").performScrollTo().assertIsDisplayed()
                compose.onNodeWithText("Sign in to workspace",useUnmergedTree=true).performScrollTo().assertIsDisplayed()
                compose.onNodeWithText("Forgot password?").performScrollTo().assertIsDisplayed()
                val button = compose.onNodeWithText("Forgot password?").fetchSemanticsNode().boundsInRoot
                val root = compose.onRoot().fetchSemanticsNode().boundsInRoot
                assertTrue(button.left>=root.left && button.right<=root.right)
            }
        } finally { instrumentation.runOnMainSync {store.clear()}; SessionStore(context).clear() }
    }
}
