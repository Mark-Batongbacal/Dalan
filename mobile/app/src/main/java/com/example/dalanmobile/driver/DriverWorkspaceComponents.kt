package com.example.dalanmobile.driver

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.dp

@Composable
internal fun DriverWorkspaceIcon(name: String) {
    val color = MaterialTheme.colorScheme.onSurface
    Canvas(Modifier.size(24.dp)) {
        val w = size.width
        val stroke = Stroke(2.dp.toPx())
        when (name) {
            "Home" -> { drawLine(color, Offset(w*.12f,w*.45f),Offset(w*.5f,w*.12f),stroke.width); drawLine(color,Offset(w*.5f,w*.12f),Offset(w*.88f,w*.45f),stroke.width); drawRect(color,Offset(w*.25f,w*.45f),Size(w*.5f,w*.43f),style=stroke) }
            "Deliveries" -> { drawRoundRect(color,Offset(w*.16f,w*.2f),Size(w*.68f,w*.64f),style=stroke); drawLine(color,Offset(w*.16f,w*.4f),Offset(w*.84f,w*.4f),stroke.width); drawLine(color,Offset(w*.5f,w*.2f),Offset(w*.5f,w*.84f),stroke.width) }
            "Route" -> { drawCircle(color,w*.12f,Offset(w*.25f,w*.2f),style=stroke); drawLine(color,Offset(w*.25f,w*.34f),Offset(w*.7f,w*.65f),stroke.width); drawCircle(color,w*.12f,Offset(w*.75f,w*.8f),style=stroke) }
            else -> { drawCircle(color,w*.18f,Offset(w*.5f,w*.28f),style=stroke); drawArc(color,180f,180f,false,Offset(w*.18f,w*.53f),Size(w*.64f,w*.5f),style=stroke) }
        }
    }
}

@Composable
internal fun DriverHomeSummary(count: Int, next: TaskInfo?, openNext: () -> Unit) {
    Card(colors=CardDefaults.cardColors(containerColor=MaterialTheme.colorScheme.primaryContainer),shape=MaterialTheme.shapes.extraLarge) {
        Column(Modifier.fillMaxWidth().padding(20.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
            Text("YOUR DELIVERY WORKSPACE",style=MaterialTheme.typography.labelSmall)
            Row(Modifier.fillMaxWidth(), horizontalArrangement=Arrangement.spacedBy(12.dp)) {
                Text(if(count == 0) "Ready for your next assignment" else "$count assigned ${if(count==1) "delivery" else "deliveries"}",modifier=Modifier.weight(1f),style=MaterialTheme.typography.headlineSmall)
                DeliveryTruckIllustration()
            }
            Text("Your confirmed stops, route and saved updates in one place.",style=MaterialTheme.typography.bodyMedium)
            if(next != null) {
                Button(onClick=openNext,modifier=Modifier.fillMaxWidth().heightIn(min=52.dp)) { Text("Open next delivery") }
                Text(next.recipientName,style=MaterialTheme.typography.labelLarge)
            }
        }
    }
}

@Composable
private fun DeliveryTruckIllustration() {
    val orange = MaterialTheme.colorScheme.primary
    val ink = MaterialTheme.colorScheme.onSurface
    val glass = MaterialTheme.colorScheme.surface
    Canvas(Modifier.width(88.dp).height(60.dp)) {
        val w = size.width; val h = size.height
        drawRoundRect(orange,Offset(w*.05f,h*.18f),Size(w*.55f,h*.57f))
        drawRoundRect(orange,Offset(w*.62f,h*.38f),Size(w*.32f,h*.37f))
        drawRect(glass,Offset(w*.66f,h*.42f),Size(w*.19f,h*.16f))
        drawCircle(ink,h*.12f,Offset(w*.24f,h*.78f)); drawCircle(ink,h*.12f,Offset(w*.78f,h*.78f))
        drawCircle(glass,h*.045f,Offset(w*.24f,h*.78f)); drawCircle(glass,h*.045f,Offset(w*.78f,h*.78f))
    }
}
