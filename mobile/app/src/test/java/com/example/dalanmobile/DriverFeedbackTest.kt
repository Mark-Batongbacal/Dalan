package com.example.dalanmobile

import com.example.dalanmobile.driver.*
import org.junit.Assert.*
import org.junit.Test

class DriverFeedbackTest {
    private val task = TaskInfo("task", "Recipient", "Warehouse", "Destination", "Assigned", "2026-11-20T00:00:00Z")
    private fun event(kind: String, failure: String? = null) = PendingEvent(owner = "driver", deliveryId = task.id,
        assignment = task.assignedAtUtc, kind = kind, payload = "", failure = failure)

    @Test fun queued_pickup_is_not_reported_as_server_accepted() {
        assertTrue(deliveryNextAction(task, listOf(event("Pickup"))).contains("saved on this device"))
        assertTrue(deliveryNextAction(task.copy(status = "PickedUp"), emptyList()).contains("capture proof"))
    }
    @Test fun queued_delivery_does_not_offer_another_confirmation() {
        assertTrue(deliveryNextAction(task, listOf(event("Pickup"), event("Deliver"))).contains("awaiting server acceptance"))
    }
    @Test fun rejected_predecessor_blocks_next_action() {
        assertTrue(deliveryNextAction(task, listOf(event("Pickup", "Assignment or delivery status changed"), event("Deliver"))).contains("review the sync queue"))
    }
    @Test fun other_assignment_events_do_not_change_current_next_action() {
        val events = listOf(event("Deliver").copy(assignment = "old"), event("Pickup").copy(deliveryId = "other"))
        assertTrue(task.assignmentEvents(events).isEmpty())
        assertTrue(deliveryNextAction(task, task.assignmentEvents(events)).contains("confirm pickup"))
    }
}
