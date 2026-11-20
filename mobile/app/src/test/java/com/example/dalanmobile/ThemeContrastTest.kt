package com.example.dalanmobile
import androidx.compose.ui.graphics.Color
import com.example.dalanmobile.ui.theme.*
import kotlin.math.pow
import org.junit.Assert.assertTrue
import org.junit.Test
class ThemeContrastTest {
    private fun luminance(c: Color): Double {
        fun channel(value: Float) = if (value <= .04045f) value / 12.92 else ((value + .055) / 1.055).pow(2.4)
        return .2126*channel(c.red)+.7152*channel(c.green)+.0722*channel(c.blue)
    }
    private fun ratio(a: Color,b: Color): Double {
        val x=luminance(a);val y=luminance(b); return (maxOf(x,y)+.05)/(minOf(x,y)+.05)
    }
    @Test fun primary_and_accent_button_labels_have_AA_contrast() {
        assertTrue(ratio(SoftOrangeDark,WarmIvory)>=4.5)
        assertTrue(ratio(SoftOrange,WarmInk)>=4.5)
        assertTrue(ratio(DarkPrimary,DarkOnPrimary)>=4.5)
    }
    @Test fun control_outlines_remain_visible_against_their_surfaces() {
        assertTrue(ratio(ControlOutline,WarmIvory)>=3.0)
        assertTrue(ratio(DarkControlOutline,DarkCard)>=3.0)
    }
    @Test fun workspace_text_and_status_labels_have_AA_contrast() {
        listOf(WarmMuted to Color.White, WarmMuted to WarmIvory,
            DarkMuted to DarkCard, SoftOrangeDeep to AccentSubtle,
            DarkAccent to DarkAccentSubtle, SemanticSuccess to SemanticSuccessBg,
            DarkSuccess to DarkSuccessBg, SemanticError to SemanticErrorBg,
            DarkError to DarkErrorBg).forEach { (text, surface) ->
            assertTrue("Text $text must be readable on $surface", ratio(text, surface) >= 4.5)
        }
    }
}
