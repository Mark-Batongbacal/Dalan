package com.example.dalanmobile.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val dark = darkColorScheme(
    primary = Color(0xFFE4AD56), onPrimary = Color(0xFF251C13),
    background = Color(0xFF17191D), onBackground = Color(0xFFF4EFE8),
    surface = Color(0xFF23262B), onSurface = Color(0xFFF4EFE8),
    surfaceVariant = Color(0xFF2D3036), onSurfaceVariant = Color(0xFFBBB3A9),
    outline = Color(0xFFBBB3A9), error = Color(0xFFFFAAA3),
    errorContainer = Color(0xFF4B272B), onErrorContainer = Color(0xFFFFAAA3),
)

private val light = lightColorScheme(
    primary = Color(0xFF9A5B20), onPrimary = Color.White,
    background = Color(0xFFF8F6F2), onBackground = Color(0xFF2C3035),
    surface = Color.White, onSurface = Color(0xFF2C3035),
    surfaceVariant = Color(0xFFF1EEE8), onSurfaceVariant = Color(0xFF64676B),
    outline = Color(0xFF64676B), error = Color(0xFFB42318),
    errorContainer = Color(0xFFFDE8E8), onErrorContainer = Color(0xFFB42318),
)

@Composable
fun DalanMobileTheme(darkTheme: Boolean = isSystemInDarkTheme(), content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (darkTheme) dark else light, typography = Typography, content = content)
}
