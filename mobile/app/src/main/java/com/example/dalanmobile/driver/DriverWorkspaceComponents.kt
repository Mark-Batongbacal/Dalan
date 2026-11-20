package com.example.dalanmobile.driver

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp

@Composable
internal fun DriverWorkspaceIcon(name: String) {
    val color = LocalContentColor.current
    Canvas(Modifier.size(24.dp)) {
        val w = size.width
        val stroke = Stroke(1.8.dp.toPx(), cap = StrokeCap.Round)
        when (name) {
            "Home" -> {
                drawLine(color, Offset(w*.12f,w*.45f), Offset(w*.5f,w*.12f), stroke.width, StrokeCap.Round)
                drawLine(color, Offset(w*.5f,w*.12f), Offset(w*.88f,w*.45f), stroke.width, StrokeCap.Round)
                drawRect(color, Offset(w*.25f,w*.45f), Size(w*.5f,w*.43f), style=stroke)
            }
            "Deliveries" -> {
                drawRoundRect(color, Offset(w*.16f,w*.2f), Size(w*.68f,w*.64f), style=stroke)
                drawLine(color, Offset(w*.16f,w*.4f), Offset(w*.84f,w*.4f), stroke.width)
                drawLine(color, Offset(w*.5f,w*.2f), Offset(w*.5f,w*.84f), stroke.width)
            }
            "Route" -> {
                drawCircle(color,w*.12f,Offset(w*.25f,w*.2f),style=stroke)
                drawLine(color,Offset(w*.25f,w*.34f),Offset(w*.7f,w*.65f),stroke.width)
                drawCircle(color,w*.12f,Offset(w*.75f,w*.8f),style=stroke)
            }
            "Check" -> {
                drawLine(color,Offset(w*.18f,w*.5f),Offset(w*.4f,w*.72f),stroke.width,StrokeCap.Round)
                drawLine(color,Offset(w*.4f,w*.72f),Offset(w*.82f,w*.25f),stroke.width,StrokeCap.Round)
            }
            "Recenter" -> {
                drawCircle(color,w*.26f,Offset(w*.5f,w*.5f),style=stroke)
                drawLine(color,Offset(w*.5f,0f),Offset(w*.5f,w),stroke.width)
                drawLine(color,Offset(0f,w*.5f),Offset(w,w*.5f),stroke.width)
            }
            else -> {
                drawCircle(color,w*.18f,Offset(w*.5f,w*.28f),style=stroke)
                drawArc(color,180f,180f,false,Offset(w*.18f,w*.53f),Size(w*.64f,w*.5f),style=stroke)
            }
        }
    }
}

@Composable
internal fun WorkspacePanel(content: @Composable ColumnScope.() -> Unit) {
    Surface(Modifier.widthIn(max = 760.dp).fillMaxWidth(), shape = MaterialTheme.shapes.extraLarge,
        color = MaterialTheme.colorScheme.surface) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp), content = content)
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun AppearanceChoice(appearance: String, onAppearance: (String) -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        listOf("System", "Light", "Dark").forEach { option ->
            FilterChip(selected = appearance == option, onClick = { onAppearance(option) },
                label = { Text(option) }, shape = CircleShape, modifier = Modifier.heightIn(min = 48.dp))
        }
    }
}

@Composable
internal fun DeliveryBadge(label: String, error: Boolean = false) {
    Surface(shape = CircleShape,
        color = if (error) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.primaryContainer,
        contentColor = if (error) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onPrimaryContainer) {
        Text(label, Modifier.padding(horizontal = 12.dp, vertical = 6.dp), style = MaterialTheme.typography.labelMedium)
    }
}

@Composable
internal fun DriverHomeSummary(count: Int, next: TaskInfo?, events: List<PendingEvent>, openNext: () -> Unit) {
    Surface(shape = MaterialTheme.shapes.extraLarge, color = MaterialTheme.colorScheme.primaryContainer,
        contentColor = MaterialTheme.colorScheme.onPrimaryContainer) {
        Column(Modifier.fillMaxWidth().padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Text(if (next == null) "Ready when you are" else "Your next stop", style = MaterialTheme.typography.titleMedium)
            Text(next?.recipientName ?: "A clear road ahead.", style = MaterialTheme.typography.headlineMedium,
                modifier = Modifier.semantics { heading() })
            if (next != null) {
                val dropoff = next.status == "PickedUp" || events.any { it.kind == "Pickup" && it.failure == null }
                Text("${if (dropoff) "Drop-off" else "Pickup"} · ${if (dropoff) next.dropoffAddress else next.pickupAddress}",
                    style = MaterialTheme.typography.bodyLarge)
                Text("$count assigned ${if (count == 1) "delivery" else "deliveries"} saved on this device", style = MaterialTheme.typography.bodySmall)
                Button(onClick = openNext, modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp), shape = CircleShape) {
                    Text("Open next delivery")
                }
            } else Text("Your next assignment will appear here after sync.", style = MaterialTheme.typography.bodyLarge)
        }
    }
}

@Composable
internal fun DeliveryListCard(task: TaskInfo, events: List<PendingEvent>, open: () -> Unit) {
    val failed = events.any { it.failure != null }
    Card(onClick = open, modifier = Modifier.fillMaxWidth(), shape = MaterialTheme.shapes.extraLarge,
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            DeliveryBadge(when {
                failed -> "Needs review"
                events.isNotEmpty() -> "Update saved on device"
                task.status == "PickedUp" -> "Ready for drop-off"
                else -> "Awaiting pickup"
            }, failed)
            Text(task.recipientName, style = MaterialTheme.typography.titleLarge)
            StopRow("Pickup", task.pickupAddress, task.status == "PickedUp")
            StopRow("Drop-off", task.dropoffAddress, false)
            Text(deliveryNextAction(task, events), style = MaterialTheme.typography.labelLarge,
                color = if (failed) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary)
        }
    }
}

@Composable
internal fun StopRow(label: String, address: String, accepted: Boolean) {
    val markerColor = MaterialTheme.colorScheme.primary
    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.Top) {
        Surface(Modifier.size(32.dp), shape = CircleShape,
            color = if (accepted) MaterialTheme.colorScheme.tertiaryContainer else MaterialTheme.colorScheme.surfaceContainerHigh,
            contentColor = if (accepted) MaterialTheme.colorScheme.onTertiaryContainer else MaterialTheme.colorScheme.primary) {
            Box(contentAlignment = Alignment.Center) {
                if (accepted) DriverWorkspaceIcon("Check")
                else Canvas(Modifier.size(10.dp)) { drawCircle(markerColor, style = Stroke(1.5.dp.toPx())) }
            }
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(address, style = MaterialTheme.typography.bodyLarge)
        }
    }
}

@Composable
internal fun ProofRequirement(label: String, ready: Boolean) {
    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
        Surface(Modifier.size(28.dp), shape = CircleShape,
            color = if (ready) MaterialTheme.colorScheme.tertiaryContainer else MaterialTheme.colorScheme.surfaceContainerHigh,
            contentColor = if (ready) MaterialTheme.colorScheme.onTertiaryContainer else MaterialTheme.colorScheme.onSurfaceVariant) {
            Box(contentAlignment = Alignment.Center) { if (ready) DriverWorkspaceIcon("Check") else Text("–") }
        }
        Text(label, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
    }
}
