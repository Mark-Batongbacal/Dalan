package com.example.dalanmobile

import android.location.Location
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.example.dalanmobile.driver.selectLocationFix
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class LocationFixSelectionTest {
    private fun fix(accuracy: Float, age: Long = 0) = Location("fixture").apply {
        latitude = 15.0; longitude = 120.0; this.accuracy = accuracy; time = System.currentTimeMillis() - age
    }

    @Test fun stale_gps_does_not_hide_a_fresh_fused_fix() = runBlocking {
        val fused = fix(8f)
        val visited = mutableListOf<String>()
        val result = selectLocationFix(listOf("gps", "fused", "network"), 50f) {
            visited += it
            if (it == "gps") fix(20f, 180_000) else fused
        }
        assertSame(fused, result)
        assertEquals(listOf("gps", "fused"), visited)
    }

    @Test fun inaccurate_gps_does_not_hide_a_precise_network_fix() = runBlocking {
        val network = fix(12f)
        val result = selectLocationFix(listOf("gps", "fused", "network"), 50f) {
            when (it) { "gps" -> fix(418f); "fused" -> fix(100f); else -> network }
        }
        assertSame(network, result)
    }

    @Test fun best_fresh_approximate_fix_is_retained_for_a_labeled_preview() = runBlocking {
        val fused = fix(100f)
        val result = selectLocationFix(listOf("gps", "fused", "network"), 50f) {
            when (it) { "gps" -> fix(418f, 180_000); "fused" -> fused; else -> fix(150f) }
        }
        assertSame(fused, result)
        assertTrue(result!!.accuracy > 50f)
    }

    @Test fun missing_or_stale_fixes_do_not_become_a_current_position() = runBlocking {
        assertNull(selectLocationFix(listOf("gps", "network"), 50f) { if (it == "gps") fix(5f, 180_000) else null })
    }
}
