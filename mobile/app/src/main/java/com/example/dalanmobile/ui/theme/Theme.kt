package com.example.dalanmobile.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.shape.RoundedCornerShape


// Soft Orange — DLN-40 Material 3 colour scheme
private val lightScheme = lightColorScheme(
    // Primary: CTA orange
    primary             = SoftOrangeDark,
    onPrimary           = androidx.compose.ui.graphics.Color.White,
    primaryContainer    = AccentSubtle,
    onPrimaryContainer  = SoftOrangeDeep,

    // Secondary: accent orange
    secondary           = SoftOrange,
    onSecondary         = WarmInk,
    secondaryContainer  = AccentSubtle,
    onSecondaryContainer = SoftOrangeDeep,

    // Tertiary: for success/positive contextual color
    tertiary            = SemanticSuccess,
    onTertiary          = WarmIvory,
    tertiaryContainer   = SemanticSuccessBg,
    onTertiaryContainer = SemanticSuccess,

    // Backgrounds & surfaces
    background          = WarmIvory,
    onBackground        = WarmInk,
    surface             = androidx.compose.ui.graphics.Color.White,
    onSurface           = WarmInk,
    surfaceVariant      = SoftPeach,
    onSurfaceVariant    = WarmMuted,
    surfaceContainer    = PeachSubtle,
    surfaceContainerHigh = PeachSubtle,
    surfaceContainerHighest = PeachSubtle,
    surfaceBright       = WarmIvory,
    surfaceDim          = SoftPeach,

    // Outlines
    outline             = ControlOutline,
    outlineVariant      = SoftBorder,

    // Error
    error               = SemanticError,
    onError             = WarmIvory,
    errorContainer      = SemanticErrorBg,
    onErrorContainer    = SemanticError,

    // Inverse
    inverseSurface      = CharcoalNav,
    inverseOnSurface    = DarkText,
    inversePrimary      = DarkAccent,
)

private val darkScheme = darkColorScheme(
    // Primary
    primary             = DarkPrimary,
    onPrimary           = DarkOnPrimary,
    primaryContainer    = DarkAccentSubtle,
    onPrimaryContainer  = DarkAccent,

    // Secondary
    secondary           = DarkAccent,
    onSecondary         = DarkOnPrimary,
    secondaryContainer  = DarkAccentSubtle,
    onSecondaryContainer = DarkAccent,

    // Tertiary
    tertiary            = DarkSuccess,
    onTertiary          = DarkCanvas,
    tertiaryContainer   = DarkSuccessBg,
    onTertiaryContainer = DarkSuccess,

    // Backgrounds & surfaces
    background          = DarkCanvas,
    onBackground        = DarkText,
    surface             = DarkCard,
    onSurface           = DarkText,
    surfaceVariant      = DarkSurface,
    onSurfaceVariant    = DarkMuted,
    surfaceContainer    = DarkCard,
    surfaceContainerHigh = DarkSubtle,
    surfaceContainerHighest = DarkSubtle,
    surfaceBright       = DarkCard,
    surfaceDim          = DarkSurface,

    // Outlines
    outline             = DarkControlOutline,
    outlineVariant      = DarkBorder,

    // Error
    error               = DarkError,
    onError             = DarkCard,
    errorContainer      = DarkErrorBg,
    onErrorContainer    = DarkError,

    // Inverse
    inverseSurface      = DarkText,
    inverseOnSurface    = DarkCanvas,
    inversePrimary      = SoftOrangeDark,
)

// Shape system: rounded to match web (20–24px cards → ~16–20dp)
private val DalanShapes = Shapes(
    extraSmall = RoundedCornerShape(8.dp),
    small      = RoundedCornerShape(12.dp),
    medium     = RoundedCornerShape(16.dp),
    large      = RoundedCornerShape(24.dp),
    extraLarge = RoundedCornerShape(24.dp),
)

@Composable
fun DalanMobileTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = if (darkTheme) darkScheme else lightScheme,
        typography  = Typography,
        shapes      = DalanShapes,
        content     = content
    )
}
