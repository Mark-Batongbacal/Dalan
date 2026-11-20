package com.example.dalanmobile.driver

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.location.Location
import android.location.LocationManager
import android.os.CancellationSignal
import android.util.Base64
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.Color
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.core.content.ContextCompat
import androidx.core.location.LocationManagerCompat
import java.io.ByteArrayOutputStream
import kotlin.coroutines.resume
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun DriverScreen(model: DriverViewModel, appearance: String, onAppearance: (String) -> Unit) {
    val state by model.state.collectAsState()
    var selected by remember(state.driverId) { mutableStateOf<String?>(null) }
    var destination by remember(state.driverId) { mutableStateOf("Home") }
    val task = state.tasks.firstOrNull { it.id == selected }
    var mapOpen by remember(task?.id, task?.assignedAtUtc) { mutableStateOf(false) }
    if (mapOpen && task != null) {
        Dialog(onDismissRequest = { mapOpen = false },
            properties = DialogProperties(usePlatformDefaultWidth = false)) {
            DriverRouteScreen(task, model) { mapOpen = false; destination = "Deliveries" }
        }
    }
    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        bottomBar = {
            if (state.signedIn && !state.needsLogin) NavigationBar(containerColor=MaterialTheme.colorScheme.surface) {
                listOf("Home", "Deliveries", "Route", "Account").forEach { item ->
                    NavigationBarItem(selected=destination == item, onClick={
                        destination = item
                        if(item == "Route") {
                            val next = state.tasks.firstOrNull()
                            selected = next?.id
                            mapOpen = next != null
                        } else selected = null
                    }, icon={ DriverWorkspaceIcon(item) }, label={ Text(item) })
                }
            }
        }
    ) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).imePadding().padding(horizontal = 20.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            item {
                Row(
                    Modifier.padding(top = 20.dp, bottom = 4.dp).fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        "Dalan",
                        style = MaterialTheme.typography.headlineSmall.copy(
                            fontWeight = androidx.compose.ui.text.font.FontWeight.ExtraBold
                        ),
                        color = MaterialTheme.colorScheme.onBackground
                    )
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        listOf("System", "Light", "Dark").forEach { option ->
                            FilterChip(
                                selected = appearance == option,
                                onClick = { onAppearance(option) },
                                label = { Text(option, style = MaterialTheme.typography.labelMedium) },
                                modifier = Modifier.heightIn(min = 36.dp)
                            )
                        }
                    }
                }
            }
            if (!state.signedIn || state.needsLogin) {
                item {
                    LoginForm(state.busy, model::login)
                    val context = LocalContext.current
                    var recoveryError by remember { mutableStateOf<String?>(null) }
                    TextButton(onClick = {
                        try {
                            context.startActivity(android.content.Intent(android.content.Intent.ACTION_VIEW,
                                android.net.Uri.parse(com.example.dalanmobile.BuildConfig.API_BASE_URL.trimEnd('/') + "/DriverRecovery/Request")))
                        } catch (_: ActivityNotFoundException) { recoveryError = "No browser available. Ask your administrator for help recovering access." }
                    }, modifier = Modifier.heightIn(min = 48.dp)) { Text("Forgot password?") }
                    recoveryError?.let { Text(it, Modifier.semantics { liveRegion = LiveRegionMode.Polite }) }
                }
            }
            if (state.signedIn) {
                if (destination == "Home" && task == null) item {
                    DriverHomeSummary(state.tasks.size, state.tasks.firstOrNull()) { selected = state.tasks.firstOrNull()?.id; destination = "Deliveries" }
                }
                item { SyncStatusCard(state) }
                item {
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        TextButton(onClick = { model.sync() }, enabled = !state.busy && !state.needsLogin, modifier = Modifier.heightIn(min = 48.dp)) {
                            Text("Sync now")
                        }
                        TextButton(onClick = {
                            selected = null
                            model.logout()
                        }, enabled = !state.busy, modifier = Modifier.heightIn(min = 48.dp)) { Text("Sign out") }
                    }
                }
                if (destination == "Account") {
                    item { Text("Account & device", Modifier.semantics { heading() }, style=MaterialTheme.typography.headlineSmall)
                        Text("Use the appearance controls above to choose your theme. Sync before signing out; saved updates remain owned by the signed-in Driver.") }
                } else if (task == null) {
                    item {
                        Text("Assigned deliveries", Modifier.semantics { heading() }, style = MaterialTheme.typography.titleLarge)
                        Text("Open a delivery to see its next action. Follow the assigned pickup and drop-off stop order.")
                        if (state.tasks.isEmpty()) Text("No assigned deliveries saved. Sync now to check the server; this list alone does not confirm there is no new work.")
                    }
                    items(state.tasks, key = { it.id }) { delivery ->
                        val events = delivery.assignmentEvents(state.events)
                        val nextAction = deliveryNextAction(delivery, events)
                        val hasFailed = events.any { it.failure != null }
                        Card(
                            onClick = { selected = delivery.id; destination = "Deliveries" },
                            modifier = Modifier.fillMaxWidth(),
                            shape = MaterialTheme.shapes.large,
                            colors = CardDefaults.cardColors(
                                containerColor = if (hasFailed)
                                    MaterialTheme.colorScheme.errorContainer
                                else
                                    MaterialTheme.colorScheme.surface,
                            ),
                            elevation = CardDefaults.cardElevation(defaultElevation = 1.dp)
                        ) {
                            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                Row(
                                    Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.Top
                                ) {
                                    DriverWorkspaceIcon("Deliveries")
                                    Spacer(Modifier.width(12.dp))
                                    Text(
                                        delivery.recipientName,
                                        style = MaterialTheme.typography.titleMedium,
                                        color = MaterialTheme.colorScheme.onSurface,
                                        modifier = Modifier.weight(1f)
                                    )
                                }
                                Text(
                                    if (delivery.status == "PickedUp" || events.any { it.kind == "Pickup" && it.failure == null })
                                        "Drop-off · ${delivery.dropoffAddress}"
                                    else
                                        "Pickup · ${delivery.pickupAddress}",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                Text(
                                    nextAction,
                                    style = MaterialTheme.typography.labelMedium,
                                    color = if (hasFailed)
                                        MaterialTheme.colorScheme.onErrorContainer
                                    else
                                        MaterialTheme.colorScheme.primary
                                )
                            }
                        }
                    }
                } else {
                    item {
                        TextButton(onClick = { selected = null }, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back to deliveries") }
                        key(task.id, task.assignedAtUtc) {
                            TaskDetails(task, task.assignmentEvents(state.events), state.busy, state.gpsProofEnabled, model) { mapOpen = true }
                        }
                    }
                }
                item { TrackingControls(state) }
                if (state.events.isNotEmpty()) {
                    item { Text("Sync queue · ${state.events.size} updates", style = MaterialTheme.typography.titleMedium) }
                    items(state.events.filter { it.failure != null }.distinctBy { it.deliveryId to it.assignment }, key = { it.sequence }) { failed ->
                        key(state.driverId) {
                            FailedUpdateCard(failed,
                                state.tasks.firstOrNull { it.id == failed.deliveryId }?.recipientName ?: failed.deliveryId,
                                state.events.count { it.deliveryId == failed.deliveryId && it.assignment == failed.assignment },
                                state.busy) { model.discard(failed) }
                            if (state.gpsProofEnabled && failed.kind == "Deliver" && failed.failure.orEmpty().startsWith("Location proof rejected")) {
                                var reason by remember(failed.sequence) { mutableStateOf("") }
                                OutlinedTextField(reason, { reason = it.take(500) }, label = { Text("Reason for GPS exception") }, modifier = Modifier.fillMaxWidth())
                                Button(onClick = { model.requestGpsReview(failed, reason) }, enabled = !state.busy && reason.trim().length >= 8, modifier = Modifier.heightIn(min = 48.dp)) { Text("Request dispatcher review") }
                            }
                        }
                    }
                }
            }
            item {
                if (state.busy) CircularProgressIndicator(Modifier.padding(8.dp))
                if (!state.signedIn) state.message?.let { Text(it, Modifier.semantics { liveRegion = LiveRegionMode.Polite }) }
            }
        }
    }
}

@Composable
private fun TrackingControls(workspace: DriverWorkspace) {
    val context = LocalContext.current
    val tracking by DriverLocationService.state.collectAsState()
    var note by remember { mutableStateOf<String?>(null) }
    fun start() {
        val location = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED
        val notifications = androidx.core.app.NotificationManagerCompat.from(context).areNotificationsEnabled()
        if (!location || !notifications) { note = "Allow location and notifications to start visible delivery tracking."; return }
        try { DriverLocationService.start(context); note = null }
        catch (_: Exception) { note = "Couldn’t start sharing. Keep the app open and try again." }
    }
    val permissions = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { start() }
    val active = tracking.active && tracking.driverId == workspace.driverId
    Card(
        Modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.large,
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
    ) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("Delivery location sharing", style = MaterialTheme.typography.titleMedium)
            Text(
                "Share your vehicle’s location with dispatch during active deliveries, including while minimized. A persistent notification lets you stop sharing. GPS samples are retained for up to 30 days when cleanup is running.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            if (active) Button(
                onClick = { DriverLocationService.stop(context) },
                colors = ButtonDefaults.buttonColors(
                    containerColor = MaterialTheme.colorScheme.error
                )
            ) { Text("Stop sharing location") }
            else Button(
                onClick = {
                    val requested = mutableListOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)
                    if (android.os.Build.VERSION.SDK_INT >= 33) requested.add(Manifest.permission.POST_NOTIFICATIONS)
                    permissions.launch(requested.toTypedArray())
                },
                enabled = !tracking.stopping && !workspace.needsLogin && workspace.tasks.any { it.status == "Assigned" || it.status == "PickedUp" },
                colors = ButtonDefaults.buttonColors(
                    containerColor = MaterialTheme.colorScheme.primary
                )
            ) {
                Text("Start sharing location")
            }
            if (tracking.driverId == workspace.driverId) tracking.message?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
            note?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error) }
        }
    }
}

@Composable
private fun LoginForm(busy: Boolean, login: (String, String) -> Unit) {
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    Card(
        Modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.large,
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
    ) {
        Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text("Driver sign-in", style = MaterialTheme.typography.titleLarge)
            OutlinedTextField(
                value = email,
                onValueChange = { email = it },
                label = { Text("Email address") },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
                modifier = Modifier.fillMaxWidth(),
                shape = MaterialTheme.shapes.medium
            )
            OutlinedTextField(
                value = password,
                onValueChange = { password = it },
                label = { Text("Password") },
                singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                modifier = Modifier.fillMaxWidth(),
                shape = MaterialTheme.shapes.medium
            )
            Button(
                onClick = { login(email, password); password = "" },
                enabled = !busy && email.isNotBlank() && password.isNotEmpty(),
                modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp),
                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.primary),
                shape = MaterialTheme.shapes.extraLarge
            ) { Text("Sign in to workspace") }
        }
    }
}

@Composable
private fun TaskDetails(task: TaskInfo, events: List<PendingEvent>, busy: Boolean, gpsProofEnabled: Boolean,
                        model: DriverViewModel, openMap: () -> Unit) {
    val pickedUp = task.status == "PickedUp" || events.any { it.kind == "Pickup" && it.failure == null }
    Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Text(task.recipientName, Modifier.semantics { heading() }, style = MaterialTheme.typography.headlineSmall)
        Card(colors=CardDefaults.cardColors(containerColor=MaterialTheme.colorScheme.surfaceVariant)) {
            Column(Modifier.fillMaxWidth().padding(16.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
                Text("Delivery progress",style=MaterialTheme.typography.titleSmall)
                Text(if(task.status == "PickedUp") "✓ Pickup accepted by server" else if(pickedUp) "◷ Pickup saved on this device" else "○ Pickup awaiting confirmation",style=MaterialTheme.typography.bodyMedium)
                Text(if(events.any { it.kind == "Deliver" }) "◷ Delivery update awaiting server acceptance" else "○ Delivery confirmation & proof",style=MaterialTheme.typography.bodyMedium)
            }
        }

        // Next action pill
        Card(
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
            shape = MaterialTheme.shapes.extraLarge
        ) {
            Text(
                deliveryNextAction(task, events),
                Modifier.padding(horizontal = 16.dp, vertical = 10.dp).semantics { liveRegion = LiveRegionMode.Polite },
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onPrimaryContainer
            )
        }

        // Stop cards
        Card(
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
            shape = MaterialTheme.shapes.large
        ) {
            Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text("${task.pickupPosition?.let { "Stop $it · " } ?: ""}Pickup",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.primary)
                    Text(task.pickupAddress, style = MaterialTheme.typography.bodyMedium)
                }
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text("${task.dropoffPosition?.let { "Stop $it · " } ?: ""}Drop-off",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.secondary)
                    Text(task.dropoffAddress, style = MaterialTheme.typography.bodyMedium)
                }
            }
        }

        TextButton(onClick = openMap, modifier = Modifier.heightIn(min = 48.dp)) { Text("View route map") }

        when {
            events.any { it.failure != null } ->
                Text("Resolve the failed update in the sync queue.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.error)
            events.any { it.kind == "Deliver" } ->
                Text("Delivery confirmation queued. It will leave your active list after server acceptance. You do not need to submit it again.",
                    style = MaterialTheme.typography.bodyMedium)
            pickedUp -> {
                if (task.status != "PickedUp")
                    Text("Pickup is queued locally. You may capture delivery proof now; the queue sends pickup before delivery.",
                        style = MaterialTheme.typography.bodySmall)
                ProofForm(task, busy, gpsProofEnabled, model)
            }
            else -> {
                Text("Confirm only after collecting the delivery at the pickup address. When offline, this saves an update for later sync.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                Button(
                    onClick = { model.record(task, "Pickup") },
                    enabled = !busy,
                    modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.primary),
                    shape = MaterialTheme.shapes.extraLarge
                ) { Text("Confirm pickup") }
            }
        }
    }
}

@Composable
private fun ProofForm(task: TaskInfo, busy: Boolean, gpsProofEnabled: Boolean, model: DriverViewModel) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var name by remember(task.id, task.assignedAtUtc) { mutableStateOf("") }
    var photo by remember(task.id, task.assignedAtUtc) { mutableStateOf<Bitmap?>(null) }
    var location by remember(task.id, task.assignedAtUtc) { mutableStateOf<Location?>(null) }
    var captureBusy by remember { mutableStateOf(false) }
    var note by remember { mutableStateOf<String?>(null) }
    var exceptionReason by remember { mutableStateOf("") }

    fun captureLocation() {
        scope.launch {
            captureBusy = true
            try {
                location = currentLocation(context)
                note = if (location == null) "No location fix. Check location settings or try near a window." else null
            } finally {
                captureBusy = false
            }
        }
    }

    val permissions = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        if (result.values.any { it }) captureLocation()
        else note = "Location permission is required to confirm delivery. If the prompt no longer appears, allow Location in Android app settings, then capture again."
    }
    val camera = rememberLauncherForActivityResult(ActivityResultContracts.TakePicturePreview()) { result ->
        if (result != null) {
            photo = result
            note = null
        } else note = if (photo == null) "No photo saved. Take a proof photo before confirming." else "Retake cancelled. The previous photo is still selected."
    }
    Text("Proof of delivery", Modifier.semantics { heading() }, style = MaterialTheme.typography.titleMedium)
    Text(
        "At drop-off, enter the recipient's name, take a proof photo, and capture location. Normally all three are required. If GPS is unavailable, request dispatcher review. Offline confirmation stays queued until server acceptance.",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant
    )
    Text(
        "Tap Confirm delivery to save this proof. Leaving this delivery before confirmation clears the unsaved photo, location and name.",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant
    )
    OutlinedTextField(
        value = name,
        onValueChange = { name = it.take(120) },
        label = { Text("Recipient confirmation") },
        modifier = Modifier.fillMaxWidth(),
        singleLine = true,
        shape = MaterialTheme.shapes.medium
    )
    // Proof status summary
    Card(
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
        shape = MaterialTheme.shapes.medium
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(
                if (name.isBlank()) "⬜  Recipient name: enter above" else "✓  Recipient name: ready",
                style = MaterialTheme.typography.bodySmall,
                color = if (name.isBlank()) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.tertiary
            )
            Text(
                if (photo == null) "⬜  Proof photo: required · opens your camera app" else "✓  Photo captured",
                style = MaterialTheme.typography.bodySmall,
                color = if (photo == null) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.tertiary
            )
            Text(
                if (location == null) "⬜  Location: required · allow location permission" else "✓  Location captured",
                style = MaterialTheme.typography.bodySmall,
                color = if (location == null) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.tertiary
            )
        }
    }
    if (gpsProofEnabled) {
        OutlinedTextField(exceptionReason, { exceptionReason = it.take(500) },
            label = { Text("GPS exception reason (optional, 8–500 characters)") }, modifier = Modifier.fillMaxWidth(), shape = MaterialTheme.shapes.medium)
        Text("Explain missing or inaccurate GPS. The photo and reason stay pending until dispatch explicitly reviews them.", style = MaterialTheme.typography.bodySmall)
    }
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        TextButton(
            onClick = {
                try { camera.launch(null) }
                catch (_: ActivityNotFoundException) { note = "No camera app is available." }
            },
            enabled = !busy && !captureBusy,
            modifier = Modifier.heightIn(min = 48.dp).weight(1f)
        ) { Text(if (photo == null) "Take proof photo" else "Retake photo") }
        TextButton(
            onClick = {
                if (ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) ==
                    PackageManager.PERMISSION_GRANTED) captureLocation()
                else permissions.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
            },
            enabled = !busy && !captureBusy,
            modifier = Modifier.heightIn(min = 48.dp).weight(1f)
        ) { Text(if (location == null) "Capture location" else "Recapture location") }
    }
    if (captureBusy) Text("Preparing proof…", Modifier.semantics { liveRegion = LiveRegionMode.Polite },
        style = MaterialTheme.typography.bodySmall)
    note?.let { Text(it, Modifier.semantics { liveRegion = LiveRegionMode.Polite },
        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error) }
    Button(onClick = {
        val bitmap = photo ?: return@Button
        val point = location
        if (point == null && (!gpsProofEnabled || exceptionReason.trim().length < 8)) return@Button
        val confirmation = name.trim()
        scope.launch {
            captureBusy = true
            try {
                val bytes = withContext(Dispatchers.Default) {
                    ByteArrayOutputStream().use { output ->
                        bitmap.compress(Bitmap.CompressFormat.JPEG, 75, output)
                        output.toByteArray()
                    }
                }
                if (bytes.isEmpty() || bytes.size > 512_000) note = "Photo could not be saved. Retake it."
                else model.record(task, "Deliver", point?.latitude, point?.longitude, confirmation,
                    Base64.encodeToString(bytes, Base64.NO_WRAP),
                    locationAccuracyMeters = if (point?.hasAccuracy() == true) point.accuracy.toDouble() else null,
                    locationCapturedAtUtc = point?.let { java.time.Instant.ofEpochMilli(it.time).toString() },
                    gpsExceptionReason = exceptionReason.trim().takeIf { it.length >= 8 })
            } finally {
                captureBusy = false
            }
        }
    }, enabled = !busy && !captureBusy && photo != null && (location != null || (gpsProofEnabled && exceptionReason.trim().length >= 8)) && name.isNotBlank(),
        modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp),
        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.primary),
        shape = MaterialTheme.shapes.extraLarge
    ) {
        Text("Confirm delivery")
    }
}

internal suspend fun currentLocation(context: Context): Location? {
    val manager = context.getSystemService(LocationManager::class.java)
    val fine = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) ==
        PackageManager.PERMISSION_GRANTED
    val providers = buildList {
        if (fine) add(LocationManager.GPS_PROVIDER to 8_000L)
        add(LocationManager.FUSED_PROVIDER to 6_000L)
        add(LocationManager.NETWORK_PROVIDER to 6_000L)
    }
    for ((provider, timeout) in providers) {
        if (!manager.isProviderEnabled(provider)) continue
        val fix = withTimeoutOrNull(timeout) {
            suspendCancellableCoroutine { continuation ->
                val cancel = CancellationSignal()
                continuation.invokeOnCancellation { cancel.cancel() }
                try {
                    LocationManagerCompat.getCurrentLocation(manager, provider, cancel, context.mainExecutor) {
                        continuation.resume(it)
                    }
                } catch (_: SecurityException) {
                    continuation.resume(null)
                } catch (_: IllegalArgumentException) {
                    continuation.resume(null)
                }
            }
        }
        if (fix != null) return fix
    }
    return null
}
