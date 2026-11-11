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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
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

@Composable
fun DriverScreen(model: DriverViewModel, appearance: String, onAppearance: (String) -> Unit) {
    val state by model.state.collectAsState()
    var selected by remember { mutableStateOf<String?>(null) }
    val task = state.tasks.firstOrNull { it.id == selected }
    var mapOpen by remember(task?.id) { mutableStateOf(false) }
    if (mapOpen && task != null) {
        Dialog(onDismissRequest = { mapOpen = false },
            properties = DialogProperties(usePlatformDefaultWidth = false)) {
            DriverRouteScreen(task, model) { mapOpen = false }
        }
    }
    Scaffold { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).imePadding().padding(horizontal = 20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item {
                Text("Dalan", Modifier.padding(top = 16.dp), style = MaterialTheme.typography.headlineLarge)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf("System", "Light", "Dark").forEach { option ->
                        FilterChip(appearance == option, { onAppearance(option) }, { Text(option) })
                    }
                }
            }
            if (!state.signedIn || state.needsLogin) {
                item { LoginForm(state.busy, model::login) }
            }
            if (state.signedIn) {
                item {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        TextButton(onClick = { model.sync() }, enabled = !state.busy && !state.needsLogin) {
                            Text("Sync now")
                        }
                        TextButton(onClick = {
                            selected = null
                            model.logout()
                        }, enabled = !state.busy) { Text("Sign out") }
                    }
                }
                if (task == null) {
                    item {
                        Text("Assigned deliveries", style = MaterialTheme.typography.titleLarge)
                        if (state.tasks.isEmpty()) Text("No assigned deliveries saved.")
                    }
                    items(state.tasks, key = { it.id }) { delivery ->
                        val events = state.events.filter {
                            it.deliveryId == delivery.id && it.assignment == delivery.assignedAtUtc
                        }
                        Card(onClick = { selected = delivery.id }, modifier = Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                Text(delivery.recipientName, style = MaterialTheme.typography.titleMedium)
                                Text(if (delivery.status == "PickedUp" || events.any { it.kind == "Pickup" && it.failure == null })
                                    "Drop-off · ${delivery.dropoffAddress}" else "Pickup · ${delivery.pickupAddress}")
                                if (events.any { it.failure != null }) Text("Sync failed")
                                else if (events.isNotEmpty()) Text("Queued")
                            }
                        }
                    }
                } else {
                    item {
                        TextButton(onClick = { selected = null }) { Text("Back to deliveries") }
                        TaskDetails(task, state.events.filter {
                            it.deliveryId == task.id && it.assignment == task.assignedAtUtc
                        }, state.busy, model) { mapOpen = true }
                    }
                }
                if (state.events.isNotEmpty()) {
                    item { Text("Sync queue · ${state.events.size} updates", style = MaterialTheme.typography.titleMedium) }
                    items(state.events.filter { it.failure != null }, key = { it.sequence }) { failed ->
                        Card(Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(16.dp)) {
                                Text("${failed.kind} failed · ${failed.failure}")
                                Text(state.tasks.firstOrNull { it.id == failed.deliveryId }?.recipientName ?: failed.deliveryId)
                                TextButton(onClick = { model.discard(failed) }, enabled = !state.busy) {
                                    Text("Discard updates for this assignment")
                                }
                            }
                        }
                    }
                }
            }
            item {
                if (state.busy) CircularProgressIndicator(Modifier.padding(8.dp))
                state.message?.let { Text(it, Modifier.semantics { liveRegion = LiveRegionMode.Polite }) }
            }
        }
    }
}

@Composable
private fun LoginForm(busy: Boolean, login: (String, String) -> Unit) {
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Driver sign-in", style = MaterialTheme.typography.titleLarge)
        OutlinedTextField(email, { email = it }, label = { Text("Email") }, singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email), modifier = Modifier.fillMaxWidth())
        OutlinedTextField(password, { password = it }, label = { Text("Password") }, singleLine = true,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password), modifier = Modifier.fillMaxWidth())
        Button(onClick = { login(email, password); password = "" },
            enabled = !busy && email.isNotBlank() && password.isNotEmpty()) { Text("Sign in") }
    }
}

@Composable
private fun TaskDetails(task: TaskInfo, events: List<PendingEvent>, busy: Boolean,
                        model: DriverViewModel, openMap: () -> Unit) {
    val pickedUp = task.status == "PickedUp" || events.any { it.kind == "Pickup" }
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(task.recipientName, style = MaterialTheme.typography.headlineSmall)
        Text("${task.pickupPosition?.let { "Stop $it · " } ?: ""}Pickup\n${task.pickupAddress}")
        Text("${task.dropoffPosition?.let { "Stop $it · " } ?: ""}Drop-off\n${task.dropoffAddress}")
        TextButton(onClick = openMap) { Text("View route map") }
        when {
            events.any { it.failure != null } -> Text("Resolve the failed update in the sync queue.")
            events.any { it.kind == "Deliver" } -> Text("Delivery confirmation queued")
            pickedUp -> ProofForm(task, busy, model)
            else -> Button(onClick = { model.record(task, "Pickup") }, enabled = !busy) { Text("Confirm pickup") }
        }
    }
}

@Composable
private fun ProofForm(task: TaskInfo, busy: Boolean, model: DriverViewModel) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var name by remember(task.id, task.assignedAtUtc) { mutableStateOf("") }
    var photo by remember(task.id, task.assignedAtUtc) { mutableStateOf<Bitmap?>(null) }
    var location by remember(task.id, task.assignedAtUtc) { mutableStateOf<Location?>(null) }
    var captureBusy by remember { mutableStateOf(false) }
    var note by remember { mutableStateOf<String?>(null) }

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
        else note = "Location permission is required to confirm delivery."
    }
    val camera = rememberLauncherForActivityResult(ActivityResultContracts.TakePicturePreview()) { result ->
        if (result != null) {
            photo = result
            note = null
        }
    }
    Text("Proof of delivery", style = MaterialTheme.typography.titleMedium)
    OutlinedTextField(name, { name = it.take(120) }, label = { Text("Recipient confirmation") },
        modifier = Modifier.fillMaxWidth(), singleLine = true)
    TextButton(onClick = {
        try {
            camera.launch(null)
        } catch (_: ActivityNotFoundException) {
            note = "No camera app is available."
        }
    }, enabled = !busy && !captureBusy) { Text(if (photo == null) "Take proof photo" else "Retake proof photo") }
    TextButton(onClick = {
        if (ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) ==
            PackageManager.PERMISSION_GRANTED) captureLocation()
        else permissions.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
    }, enabled = !busy && !captureBusy) {
        Text(if (location == null) "Capture location" else "Recapture location")
    }
    if (photo != null) Text("Photo captured")
    if (location != null) Text("Location captured")
    note?.let { Text(it, Modifier.semantics { liveRegion = LiveRegionMode.Polite }) }
    Button(onClick = {
        val bitmap = photo ?: return@Button
        val point = location ?: return@Button
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
                else model.record(task, "Deliver", point.latitude, point.longitude, confirmation,
                    Base64.encodeToString(bytes, Base64.NO_WRAP))
            } finally {
                captureBusy = false
            }
        }
    }, enabled = !busy && !captureBusy && photo != null && location != null && name.isNotBlank()) {
        Text("Confirm delivery")
    }
}

private suspend fun currentLocation(context: Context): Location? {
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
