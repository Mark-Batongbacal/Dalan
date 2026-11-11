package com.example.dalanmobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import com.example.dalanmobile.driver.DriverScreen
import com.example.dalanmobile.driver.DriverViewModel
import com.example.dalanmobile.ui.theme.DalanMobileTheme
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        val model = ViewModelProvider(this)[DriverViewModel::class.java]
        val preferences = getSharedPreferences("appearance", MODE_PRIVATE)
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.STARTED) {
                while (true) {
                    if (model.state.value.signedIn && !model.state.value.needsLogin) model.sync()
                    delay(45_000)
                }
            }
        }
        setContent {
            var appearance by remember { mutableStateOf(preferences.getString("mode", "System") ?: "System") }
            val dark = when (appearance) {
                "Dark" -> true
                "Light" -> false
                else -> isSystemInDarkTheme()
            }
            DalanMobileTheme(dark) {
                DriverScreen(model, appearance) { choice ->
                    appearance = choice
                    preferences.edit().putString("mode", choice).apply()
                }
            }
        }
    }
}
