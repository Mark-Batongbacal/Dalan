package com.example.dalanmobile

import com.example.dalanmobile.driver.TaskEvent
import com.example.dalanmobile.driver.driverJson
import org.junit.Assert.*
import org.junit.Test

class DriverProofPayloadTest {
    @Test fun existing_room_payload_remains_readable_without_fix_metadata() {
        val event = driverJson.decodeFromString<TaskEvent>("""{"eventId":"event","assignedAtUtc":"2026-11-20T00:00:00Z","capturedAtUtc":"2026-11-20T01:00:00Z","kind":"Deliver","latitude":14.0,"longitude":121.0,"photo":"jpeg"}""")
        assertNull(event.locationAccuracyMeters)
        assertNull(event.locationCapturedAtUtc)
        assertEquals("jpeg", event.photo)
    }

    @Test fun queued_proof_preserves_the_original_fix_time_and_accuracy() {
        val event = TaskEvent("event", "assignment", "2026-11-20T01:00:00Z", "Deliver",
            14.0, 121.0, "Recipient", "jpeg", 8.5, "2026-11-20T00:59:50Z")
        val queued = driverJson.encodeToString(event)
        assertTrue(queued.contains("locationAccuracyMeters"))
        assertTrue(queued.contains("locationCapturedAtUtc"))
        assertEquals(event, driverJson.decodeFromString<TaskEvent>(queued))
    }
}
