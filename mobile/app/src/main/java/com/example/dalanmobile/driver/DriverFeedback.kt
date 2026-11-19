package com.example.dalanmobile.driver

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import java.time.ZoneId
import java.time.format.DateTimeFormatter

internal fun TaskInfo.assignmentEvents(events: List<PendingEvent>) = events.filter {
    it.deliveryId == id && it.assignment == assignedAtUtc
}

internal fun deliveryNextAction(task: TaskInfo, events: List<PendingEvent>): String = when {
    events.any { it.failure != null } -> "Action needed · review the sync queue"
    events.any { it.kind == "Deliver" } -> "Delivery saved on this device · awaiting server acceptance"
    task.status == "PickedUp" -> "Next: drop off and capture proof"
    events.any { it.kind == "Pickup" } -> "Pickup saved on this device · prepare drop-off proof"
    else -> "Next: collect from pickup, then confirm pickup"
}

@Composable
internal fun SyncStatusCard(state: DriverWorkspace) {
    val failed = state.events.count { it.failure != null }
    val title = when {
        state.needsLogin -> "Sign-in required"
        state.syncing -> "Syncing with server…"
        state.busy -> "Working…"
        failed > 0 -> "Updates need review"
        state.events.isNotEmpty() -> "Updates queued on this device"
        state.message == "Up to date" && state.lastSyncedAt != null -> "Synchronized"
        else -> "Saved delivery workspace"
    }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp).semantics { liveRegion = LiveRegionMode.Polite },
            verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(title, Modifier.semantics { heading() }, style = MaterialTheme.typography.titleMedium)
            if (state.events.isNotEmpty()) Text("${state.events.size} saved updates · $failed rejected. A saved update is not yet accepted by the server.")
            if (state.needsLogin) Text("Sign in again to sync. Saved updates remain with the Driver account that created them.")
            else if (failed > 0) Text("Rejected assignments stay blocked. Sync now refreshes other work; review the reason below before discarding anything.")
            else if (state.events.isNotEmpty()) Text("Keep this app open and use Sync now when connected. Do not clear app storage or uninstall while updates are pending.")
            state.lastSyncedAt?.let {
                Text("Last server refresh: ${DateTimeFormatter.ofPattern("dd MMM, HH:mm:ss z").withZone(ZoneId.systemDefault()).format(it)}")
            }
            state.message?.takeIf { it != "Up to date" }?.let { Text(it) }
        }
    }
}

@Composable
internal fun FailedUpdateCard(event: PendingEvent, recipient: String, updateCount: Int,
                              busy: Boolean, discard: () -> Unit) {
    var confirming by remember(event.owner, event.deliveryId, event.assignment) { mutableStateOf(false) }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(if (event.failure == "Assignment or delivery status changed") "Assignment changed" else "Update rejected",
                Modifier.semantics { heading() }, style = MaterialTheme.typography.titleMedium)
            Text(recipient)
            Text(event.failure.orEmpty())
            Text("$updateCount saved updates for this assignment are blocked. Contact dispatch if the assignment or proof needs correction; Sync now will not force rejected updates through.")
            TextButton(onClick = { confirming = true }, enabled = !busy, modifier = Modifier.heightIn(min = 48.dp)) {
                Text("Review discard options")
            }
        }
    }
    if (confirming) AlertDialog(
        onDismissRequest = { if (!busy) confirming = false },
        title = { Text("Discard saved updates?") },
        text = { Text("Remove all $updateCount saved updates for $recipient on this assignment, including any unsent proof photo. This cannot be undone and does not complete or cancel the server delivery. Keep them if dispatch still needs the proof.") },
        confirmButton = {
            TextButton(onClick = { confirming = false; discard() }, enabled = !busy, modifier = Modifier.heightIn(min = 48.dp)) { Text("Discard saved updates") }
        },
        dismissButton = {
            TextButton(onClick = { confirming = false }, enabled = !busy, modifier = Modifier.heightIn(min = 48.dp)) { Text("Keep updates") }
        }
    )
}
