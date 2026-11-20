package com.example.dalanmobile.driver

import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.saveable.rememberSaveable
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
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.background
import androidx.compose.ui.Alignment
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.contentDescription
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
    var selected by rememberSaveable(state.driverId) { mutableStateOf<String?>(null) }
    var destination by rememberSaveable(state.driverId) { mutableStateOf("Home") }
    val task = state.tasks.firstOrNull { it.id == selected }
    var mapOpen by remember(task?.id, task?.assignedAtUtc) { mutableStateOf(false) }
    val authenticated = state.signedIn && !state.needsLogin
    fun goTo(item: String) {
        destination = item
        selected = if (item == "Route") state.tasks.firstOrNull()?.id else null
        mapOpen = item == "Route" && selected != null
    }
    BackHandler(enabled = authenticated && (selected != null || destination != "Home") && !mapOpen) {
        if (selected != null) { selected = null; destination = "Deliveries" }
        else destination = "Home"
    }
    if (mapOpen && task != null) {
        Dialog(onDismissRequest = { mapOpen = false },
            properties = DialogProperties(usePlatformDefaultWidth = false)) {
            DriverRouteScreen(task, model) { mapOpen = false }
        }
    }
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val expanded = maxWidth >= 600.dp
        Row(Modifier.fillMaxSize()) {
            if (authenticated && expanded) NavigationRail(
                containerColor = MaterialTheme.colorScheme.surface,
                modifier = Modifier.fillMaxHeight(),
                header = { androidx.compose.foundation.Image(androidx.compose.ui.res.painterResource(com.example.dalanmobile.R.drawable.dalan_logo), "Dalan", Modifier.padding(12.dp).size(48.dp)) }
            ) {
                listOf("Home", "Deliveries", "Route", "Profile").forEach { item ->
                    NavigationRailItem(selected = destination == item, onClick = { goTo(item) },
                        colors = NavigationRailItemDefaults.colors(selectedIconColor = MaterialTheme.colorScheme.primary, selectedTextColor = MaterialTheme.colorScheme.primary, indicatorColor = MaterialTheme.colorScheme.primaryContainer),
                        icon = { DriverWorkspaceIcon(item) }, label = { Text(item) })
                }
            }
            Scaffold(
                modifier = Modifier.weight(1f),
                containerColor = MaterialTheme.colorScheme.background,
                bottomBar = {
                    if (authenticated && !expanded) NavigationBar(containerColor = MaterialTheme.colorScheme.surface) {
                        listOf("Home", "Deliveries", "Route", "Profile").forEach { item ->
                            NavigationBarItem(selected = destination == item, onClick = { goTo(item) },
                                colors = NavigationBarItemDefaults.colors(selectedIconColor = MaterialTheme.colorScheme.primary, selectedTextColor = MaterialTheme.colorScheme.primary, indicatorColor = MaterialTheme.colorScheme.primaryContainer),
                                icon = { DriverWorkspaceIcon(item) }, label = { Text(item) })
                        }
                    }
                }
            ) { padding ->
                // A separate list state per destination keeps Account scrolling out of the delivery workflow.
                key(destination, selected, authenticated) {
                    LazyColumn(
                        Modifier.padding(padding).imePadding().fillMaxSize(),
                        contentPadding = PaddingValues(horizontal = if (expanded) 40.dp else 20.dp, vertical = 20.dp),
                        verticalArrangement = Arrangement.spacedBy(20.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        state = rememberLazyListState()
                    ) {
                        item {
                            Row(Modifier.widthIn(max = 760.dp).fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.SpaceBetween) {
                                Column(Modifier.weight(1f)) {
                                    if (!authenticated || destination == "Home") DalanBrand()
                                    else Text(if (task != null) "Delivery details" else destination,
                                        style = MaterialTheme.typography.headlineMedium, modifier = Modifier.semantics { heading() })

                                }
                                if (task != null) TextButton(onClick = { selected = null; destination = "Deliveries" }, modifier = Modifier.heightIn(min = 48.dp)) {
                                    Text("Back to deliveries")
                                }
                                else if (authenticated && destination == "Home") FilledTonalIconButton(onClick = { goTo("Profile") }, modifier = Modifier.size(48.dp)) {
                                    Box(Modifier.semantics { contentDescription = "Open profile" }) { DriverWorkspaceIcon("Profile") }
                                }
                            }
                        }
                        if (!authenticated) {
                            item { Column(Modifier.widthIn(max = 520.dp).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(20.dp)) {
                                AppearanceChoice(appearance, onAppearance)
                                LoginForm(state.busy, model::login)
                                if (state.needsLogin) SyncStatusCard(state)
                                val context = LocalContext.current
                                var recoveryError by remember { mutableStateOf<String?>(null) }
                                TextButton(onClick = {
                                    try { context.startActivity(android.content.Intent(android.content.Intent.ACTION_VIEW,
                                        android.net.Uri.parse(com.example.dalanmobile.BuildConfig.API_BASE_URL.trimEnd('/') + "/DriverRecovery/Request"))) }
                                    catch (_: ActivityNotFoundException) { recoveryError = "No browser available. Ask your administrator for help recovering access." }
                                }, modifier = Modifier.heightIn(min = 48.dp)) { Text("Forgot password?") }
                                recoveryError?.let { Text(it, Modifier.semantics { liveRegion = LiveRegionMode.Polite }, color = MaterialTheme.colorScheme.error) }
                                state.message?.let { Text(it, Modifier.semantics { liveRegion = LiveRegionMode.Polite }, color = MaterialTheme.colorScheme.error) }
                            } }
                        } else {
                            if (destination == "Profile") {
                                item { WorkspacePanel {
                                    Text("Appearance", style = MaterialTheme.typography.titleLarge)
                                    Text("System follows your device.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                                    AppearanceChoice(appearance, onAppearance)
                                } }
                            }
                            if (task == null && destination == "Home") item {
                                Box(Modifier.widthIn(max = 760.dp).fillMaxWidth()) {
                                    DriverHomeSummary(state.tasks.size, state.tasks.firstOrNull(), state.tasks.firstOrNull()?.assignmentEvents(state.events).orEmpty()) {
                                        selected = state.tasks.firstOrNull()?.id; destination = "Deliveries"
                                    }
                                }
                            }
                            if (task == null) item { Box(Modifier.widthIn(max = 760.dp).fillMaxWidth()) { SyncStatusCard(state, model::sync) } }
                            when {
                                destination == "Profile" -> {
                                    item { Box(Modifier.widthIn(max = 760.dp).fillMaxWidth()) { TrackingControls(state) } }
                                    item { WorkspacePanel {
                                        Text("Saved updates stay yours", style = MaterialTheme.typography.titleMedium)
                                        Text("Sync first. Unsent updates stay on this device with your Driver account.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                        OutlinedButton(onClick = { selected = null; model.logout() }, enabled = !state.busy,
                                            modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) { Text("Sign out") }
                                    } }
                                }
                                task != null -> item { Column(Modifier.widthIn(max = 760.dp).fillMaxWidth()) {
                                    key(task.id, task.assignedAtUtc) { TaskDetails(task, task.assignmentEvents(state.events), state.busy, state.gpsProofEnabled, model) { mapOpen = true } }
                                } }
                                else -> {
                                    item { Column(Modifier.widthIn(max = 760.dp).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                        Text(if (destination == "Home") "Coming up" else "Assigned deliveries", Modifier.semantics { heading() }, style = MaterialTheme.typography.titleLarge)
                                        Text("${state.tasks.size} saved ${if (state.tasks.size == 1) "delivery" else "deliveries"} · saved on device", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                    } }
                                    if (state.tasks.isEmpty()) item { WorkspacePanel {
                                        DriverWorkspaceIcon("Deliveries")
                                        Text("No deliveries saved", style = MaterialTheme.typography.titleLarge)
                                        Text("Sync to check for new assignments.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                                    } }
                                    items(if (destination == "Home") state.tasks.drop(1) else state.tasks, key = { it.id }) { delivery ->
                                        Box(Modifier.widthIn(max = 760.dp).fillMaxWidth()) {
                                            DeliveryListCard(delivery, delivery.assignmentEvents(state.events)) { selected = delivery.id; destination = "Deliveries" }
                                        }
                                    }
                                }
                            }
                            if (state.events.isNotEmpty()) {
                                item { Column(Modifier.widthIn(max = 760.dp).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                    Text("Sync queue · ${state.events.size} updates", Modifier.semantics { heading() }, style = MaterialTheme.typography.titleMedium)
                                    Text("Saved locally. Awaiting server acceptance.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                } }
                                items(state.events.filter { it.failure != null }.distinctBy { it.deliveryId to it.assignment }, key = { it.sequence }) { failed ->
                                    Column(Modifier.widthIn(max = 760.dp).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                                        FailedUpdateCard(failed, state.tasks.firstOrNull { it.id == failed.deliveryId }?.recipientName ?: failed.deliveryId,
                                            state.events.count { it.deliveryId == failed.deliveryId && it.assignment == failed.assignment }, state.busy) { model.discard(failed) }
                                        if (state.gpsProofEnabled && failed.kind == "Deliver" && failed.failure.orEmpty().startsWith("Location proof rejected")) {
                                            var reason by remember(failed.sequence) { mutableStateOf("") }
                                            OutlinedTextField(reason, { reason = it.take(500) }, label = { Text("Reason for GPS exception") }, modifier = Modifier.fillMaxWidth())
                                            Button(onClick = { model.requestGpsReview(failed, reason) }, enabled = !state.busy && reason.trim().length >= 8,
                                                modifier = Modifier.heightIn(min = 48.dp)) { Text("Request dispatcher review") }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
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
            Text("Location sharing", style = MaterialTheme.typography.titleMedium)
            Text(
                "Share with dispatch during deliveries, including in the background. Stop via the notification. GPS history: up to 30 days when cleanup runs.",
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
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
    ) {
        Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text("Sign in for your next stop", style = MaterialTheme.typography.headlineSmall)
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
                shape = CircleShape
            ) { Text("Sign in to workspace") }
        }
    }
}

@Composable
private fun TaskDetails(task: TaskInfo, events: List<PendingEvent>, busy: Boolean, gpsProofEnabled: Boolean,
                        model: DriverViewModel, openMap: () -> Unit) {
    val pickedUp = task.status == "PickedUp" || events.any { it.kind == "Pickup" && it.failure == null }
    Column(verticalArrangement = Arrangement.spacedBy(20.dp)) {
        Text(task.recipientName, Modifier.semantics { heading() }, style = MaterialTheme.typography.headlineMedium)
        DeliveryBadge(when {
            events.any { it.failure != null } -> "Needs review"
            events.any { it.kind == "Deliver" } -> "Delivery saved on device"
            task.status == "PickedUp" -> "Pickup accepted by server"
            pickedUp -> "Pickup saved on device"
            else -> "Awaiting pickup"
        }, events.any { it.failure != null })
        Text(deliveryNextAction(task, events), Modifier.semantics { liveRegion = LiveRegionMode.Polite },
            style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.primary)
        WorkspacePanel {
            Text("Delivery stops", style = MaterialTheme.typography.titleMedium)
            StopRow("${task.pickupPosition?.let { "Stop $it · " } ?: ""}Pickup", task.pickupAddress, task.status == "PickedUp")
            HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
            StopRow("${task.dropoffPosition?.let { "Stop $it · " } ?: ""}Drop-off", task.dropoffAddress, false)
            OutlinedButton(onClick = openMap, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp), shape = CircleShape) {
                Text("View route map")
            }
        }

        when {
            events.any { it.failure != null } ->
                Text("Resolve the failed update in the sync queue.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.error)
            events.any { it.kind == "Deliver" } ->
                Text("Confirmation queued. Removed after server acceptance; don’t resubmit.",
                    style = MaterialTheme.typography.bodyMedium)
            pickedUp -> {
                if (task.status != "PickedUp")
                    Text("Pickup saved locally. Proof will sync after pickup.",
                        style = MaterialTheme.typography.bodySmall)
                ProofForm(task, busy, gpsProofEnabled, model)
            }
            else -> {
                Text("Confirm after collecting at pickup. Offline updates sync later.",
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
        "Add recipient, photo and location. Offline proof awaits server acceptance.",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant
    )
    Text(
        "Confirm to save. Leaving clears unsaved proof.",
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
    WorkspacePanel {
        ProofRequirement(if (name.isBlank()) "Recipient name required" else "Recipient name ready", name.isNotBlank())
        ProofRequirement(if (photo == null) "Proof photo required" else "Photo captured", photo != null)
        ProofRequirement(if (location == null) "Location required" else "Location captured", location != null)
    }
    if (gpsProofEnabled) {
        OutlinedTextField(exceptionReason, { exceptionReason = it.take(500) },
            label = { Text("GPS exception reason (optional, 8–500 characters)") }, modifier = Modifier.fillMaxWidth(), shape = MaterialTheme.shapes.medium)
        Text("No accurate GPS? Add a reason. Proof awaits dispatcher review.", style = MaterialTheme.typography.bodySmall)
    }
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        OutlinedButton(
            onClick = {
                try { camera.launch(null) }
                catch (_: ActivityNotFoundException) { note = "No camera app is available." }
            },
            enabled = !busy && !captureBusy,
            modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)
        ) { Text(if (photo == null) "Take proof photo" else "Retake photo") }
        OutlinedButton(
            onClick = {
                if (ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) ==
                    PackageManager.PERMISSION_GRANTED) captureLocation()
                else permissions.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
            },
            enabled = !busy && !captureBusy,
            modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)
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

internal suspend fun currentLocation(context: Context, maximumAccuracyMeters: Float = Float.MAX_VALUE): Location? {
    val manager = context.getSystemService(LocationManager::class.java)
    val fine = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) ==
        PackageManager.PERMISSION_GRANTED
    val providers = buildList {
        if (fine) add(LocationManager.GPS_PROVIDER)
        add(LocationManager.FUSED_PROVIDER)
        add(LocationManager.NETWORK_PROVIDER)
    }
    return selectLocationFix(providers, maximumAccuracyMeters) { provider ->
        if (!manager.isProviderEnabled(provider)) null else withTimeoutOrNull(if (provider == LocationManager.GPS_PROVIDER) 8_000L else 6_000L) {
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
    }
}
