package com.example.dalanmobile.driver

import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue

import androidx.compose.foundation.background
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.stopScroll
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.graphics.luminance
import com.example.dalanmobile.ui.theme.DarkInfo
import com.example.dalanmobile.ui.theme.SemanticInfo
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.runtime.collectAsState
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import android.Manifest
import android.content.pm.PackageManager
import android.os.SystemClock
import android.view.MotionEvent
import kotlinx.coroutines.delay
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import java.text.NumberFormat
import kotlinx.coroutines.CancellationException
import org.maplibre.android.MapLibre
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.geometry.LatLngBounds
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.PropertyFactory
import org.maplibre.android.style.sources.GeoJsonSource
import org.maplibre.geojson.FeatureCollection
import org.maplibre.geojson.Feature
import org.maplibre.geojson.LineString
import org.maplibre.geojson.Point
import kotlin.math.roundToInt

private const val BASEMAP = "https://tiles.openfreemap.org/styles/"
private const val EMPTY_STYLE = """{"version":8,"sources":{},"layers":[{"id":"background","type":"background","paint":{"background-color":"#F1EEE8"}}]}"""
private const val ROUTE_SOURCE = "dalan-route"

@Composable
fun DriverRouteScreen(task: TaskInfo, model: DriverViewModel, close: () -> Unit) {
    val pickup = stopPoint(task.pickupLatitude, task.pickupLongitude)
    val dropoff = stopPoint(task.dropoffLatitude, task.dropoffLongitude)
    var route by remember(task.id) { mutableStateOf<DriverRoadRoute?>(null) }
    var line by remember(task.id) { mutableStateOf<LineString?>(null) }
    var remainingLines by remember(task.id) { mutableStateOf<List<LineString>>(emptyList()) }
    var status by remember(task.id) { mutableStateOf("Loading road route…") }
    var mapLoaded by remember(task.id) { mutableStateOf<Boolean?>(null) }
    var attempt by remember(task.id) { mutableIntStateOf(0) }
    var mapFocused by remember(task.id) { mutableStateOf(false) }
    var mapBounds by remember { mutableStateOf(Rect.Zero) }
    var screenOrigin by remember { mutableStateOf(Offset.Zero) }
    val pageScroll = rememberScrollState()
    LaunchedEffect(mapFocused) { if (mapFocused) pageScroll.stopScroll() }
    val context = LocalContext.current
    val workspace by model.state.collectAsState()
    val navigationLifecycle = LocalLifecycleOwner.current.lifecycle
    val assignedStops = NavigationPolicy.remainingStops(workspace.tasks)
    var navigating by remember { mutableStateOf(false) }
    var navigation by remember { mutableStateOf<DriverNavigationRoute?>(null) }
    var position by remember { mutableStateOf<Point?>(null) }
    var guidance by remember { mutableStateOf("") }
    var lastRequest by remember { mutableStateOf(0L) }
    var shapeProgress by remember { mutableIntStateOf(0) }
    var remainingMeters by remember { mutableStateOf<Double?>(null) }
    var remainingSeconds by remember { mutableStateOf<Double?>(null) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        navigating = result.values.any { it }
        if (!navigating) status = "Location permission denied. The assigned-stop preview remains available."
    }
    LaunchedEffect(navigating, workspace.tasks, attempt) {
        if (!navigating) return@LaunchedEffect
        navigation = null; remainingLines = emptyList(); guidance = ""; lastRequest = 0L
        while (true) {
            if (!navigationLifecycle.currentState.isAtLeast(Lifecycle.State.RESUMED)) { delay(15_000); continue }
            try {
                val fix = currentLocation(context)
                if (fix == null || !fix.hasAccuracy() || fix.accuracy > 50 ||
                    java.time.Instant.now().toEpochMilli() - fix.time !in 0..120_000) {
                    status = "Navigation paused. Get a fresh, precise location. Shown route is a preview."
                    guidance = ""; position = null
                } else {
                    val point = NavigationPoint(fix.latitude, fix.longitude)
                    position = Point.fromLngLat(fix.longitude, fix.latitude)
                    val points = line?.coordinates().orEmpty().map { NavigationPoint(it.latitude(), it.longitude()) }
                    val now = SystemClock.elapsedRealtime()
                    if (lastRequest == 0L || NavigationPolicy.shouldReroute(now - lastRequest,
                            NavigationPolicy.distanceToRoute(point, points), navigation != null)) {
                        lastRequest = now
                        val result = model.navigate(fix)
                        if (result == null) { status = "No remaining assigned stops. Sync your deliveries."; guidance = ""; break }
                        val allLegs = navigationLines(result, point)
                        require(allLegs.isNotEmpty())
                        navigation = result; route = result.route; line = allLegs.first(); shapeProgress = 0
                        remainingLines = allLegs
                    }
                    if (navigation == null) { delay(15_000); continue }
                    val activePoints = line?.coordinates().orEmpty().map { NavigationPoint(it.latitude(), it.longitude()) }
                    val distance = NavigationPolicy.distanceToRoute(point, activePoints)
                    val nearest = activePoints.indices.minByOrNull { NavigationPolicy.distance(point, activePoints[it]) } ?: 0
                    if (distance <= NavigationPolicy.OFF_ROUTE_METERS) shapeProgress = maxOf(shapeProgress, nearest)
                    remainingMeters = if (activePoints.isEmpty()) null else NavigationPolicy.distance(point, activePoints[shapeProgress.coerceAtMost(activePoints.lastIndex)]) +
                        activePoints.drop(shapeProgress).zipWithNext().sumOf { (a,b) -> NavigationPolicy.distance(a,b) }
                    remainingSeconds = route?.let { if (it.distanceMeters > 0) it.durationSeconds * ((remainingMeters ?: it.distanceMeters) / it.distanceMeters).coerceIn(0.0,1.0) else 0.0 }
                    guidance = if (distance > NavigationPolicy.OFF_ROUTE_METERS) "Off route. Waiting for the reroute cooldown."
                        else route?.maneuvers?.firstOrNull { it.endShapeIndex >= shapeProgress }?.instruction
                            ?: "No turn instructions available. Follow the route preview carefully."
                    status = if (navigation?.stops == null) "Next leg only. Full route needs the updated server."
                        else "ETA estimated. Follow road signs."
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                guidance = ""
                status = when ((error as? DriverHttpException)?.status) {
                    401,403 -> "Sign in again to navigate."
                    422 -> "No route or fresh accurate position. Check location and retry."
                    429 -> "Routing request limit reached. Wait before retrying."
                    else -> "Navigation unavailable. Shown route is a preview; no offline guidance."
                }
            }
            delay(15_000)
        }
    }
    LaunchedEffect(task.id, task.assignedAtUtc, attempt, pickup, dropoff, navigating) {
        if (navigating) return@LaunchedEffect
        if (pickup == null || dropoff == null) {
            status = "Sync deliveries to load the map stops."
            return@LaunchedEffect
        }
        status = "Loading road route…"
        try {
            val result = model.route(task.id)
            val geometry = LineString.fromPolyline(result.encodedPolyline6, 6)
            require(geometry.coordinates().size >= 2)
            line = geometry
            route = result
            status = ""
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (error: Exception) {
            route = null
            line = null
            status = when ((error as? DriverHttpException)?.status) {
                401, 403 -> "Sign in again to load the road route."
                404 -> "This delivery is no longer assigned to you."
                422 -> "No road route was found between these stops."
                else -> "Road route unavailable. Check connection and retry."
            }
        }
    }

    Scaffold(containerColor = MaterialTheme.colorScheme.background) { padding ->
        BoxWithConstraints(Modifier.fillMaxSize().padding(padding)
            .onGloballyPositioned { screenOrigin = it.positionInRoot() }
            .pointerInput(task.id) {
                // Observe before the native map handles the gesture, without consuming its pan/pinch events.
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                    mapFocused = mapBounds.contains(down.position + screenOrigin)
                }
            }) {
            val mapHeight = (maxHeight * .53f).coerceIn(240.dp, 560.dp)
            Column(Modifier.fillMaxSize().verticalScroll(pageScroll, enabled = !mapFocused).padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)) {
                TextButton(onClick = close, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back to delivery") }
                Text(if (navigating) "Current route" else "Pickup to drop-off", Modifier.semantics { heading() }, style = MaterialTheme.typography.headlineMedium)
                Text(if (navigating) navigation?.let { active -> workspace.tasks.firstOrNull { it.id == active.deliveryId }?.recipientName } ?: "Next assigned stop" else task.recipientName,
                    style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                val mapStops = if (navigating) navigation?.stops ?: assignedStops else listOfNotNull(
                    pickup?.let { DriverNavigationStop(task.id, "Pickup", task.pickupAddress, NavigationPoint(it.latitude(), it.longitude())) },
                    dropoff?.let { DriverNavigationStop(task.id, "Drop-off", task.dropoffAddress, NavigationPoint(it.latitude(), it.longitude())) })
                if (mapStops.isNotEmpty()) {
                    key(task.id, attempt) {
                        DeliveryMap(mapStops, if (navigating) remainingLines else listOfNotNull(line),
                            Modifier.fillMaxWidth().height(mapHeight).clip(MaterialTheme.shapes.extraLarge)
                                .onGloballyPositioned { mapBounds = it.boundsInRoot() }, position, mapFocused) { mapLoaded = it }
                    }
                    DriverRouteLegend(navigating)
                    if (mapLoaded == false) {
                        Text("Basemap unavailable · street detail hidden.", style = MaterialTheme.typography.bodySmall)
                    }
                    if (mapLoaded == false || (route == null && status != "Loading road route…")) {
                        TextButton(onClick = { mapFocused = false; mapLoaded = null; attempt++ }, modifier = Modifier.heightIn(min = 48.dp)) {
                            Text(if (route == null) "Retry route" else "Retry map")
                        }
                    }
                }
                WorkspacePanel {
                    Text(navigation?.let { "${it.stopKind}: ${it.address}" } ?: "Assigned stops", style = MaterialTheme.typography.titleLarge)
                    route?.let {
                        val distance = NumberFormat.getNumberInstance().apply { maximumFractionDigits = 1 }.format(it.distanceMeters / 1000)
                        val minutes = (it.durationSeconds / 60).roundToInt().coerceAtLeast(1)
                        Text("$distance km · about $minutes min by road", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.primary)
                    }
                    if (navigating && guidance.isNotBlank() && remainingMeters != null) {
                        Text("Approximately ${(remainingMeters!! / 1000 * 10).roundToInt() / 10.0} km remaining · ${((remainingSeconds ?: 0.0) / 60).roundToInt().coerceAtLeast(1)} min estimated")
                    }
                    if (guidance.isNotBlank()) Text(guidance, Modifier.semantics { liveRegion = LiveRegionMode.Polite }, style = MaterialTheme.typography.titleMedium)
                    if (status.isNotEmpty()) Text(status, Modifier.semantics { liveRegion = LiveRegionMode.Polite }, style = MaterialTheme.typography.bodyMedium)
                    if (navigating) {
                        Text("${mapStops.size} remaining stops", style = MaterialTheme.typography.titleMedium)
                        mapStops.forEachIndexed { index, stop -> Text("${index + 1}. ${stop.stopKind} · ${stop.address}", style = MaterialTheme.typography.bodySmall) }
                    }
                    Text(if (navigating) "Uses location while this map is open. Dispatch sharing is separate."
                        else "Start navigation from your location.",
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Button(onClick = {
                        mapFocused = false
                        if (navigating) { navigating = false; navigation = null; position = null; guidance = "" }
                        else if (ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED) navigating = true
                        else permission.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
                    }, modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp), shape = CircleShape) {
                        Text(if (navigating) "Stop navigation" else "Navigate from current location")
                    }
                }
            }
        }
    }
}

@Composable
internal fun DriverRouteLegend(navigating: Boolean) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        if (navigating) {
            MapStopLegend("Current position", driverPositionColor())
        }
        MapStopLegend("Pickup", MaterialTheme.colorScheme.tertiary)
        MapStopLegend("Drop-off", MaterialTheme.colorScheme.primary)
    }
}

@Composable
private fun driverPositionColor() =
    if (MaterialTheme.colorScheme.background.luminance() < .5f) DarkInfo else SemanticInfo

@Composable
private fun MapStopLegend(label: String, color: Color) {
    Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(10.dp).background(color, CircleShape))
        Text(label)
    }
}

private fun stopPoint(latitude: Double?, longitude: Double?): Point? =
    if (latitude != null && longitude != null) Point.fromLngLat(longitude, latitude) else null

@Composable
internal fun DeliveryMap(stops: List<DriverNavigationStop>, lines: List<LineString>, modifier: Modifier,
                        currentPosition: Point? = null, focused: Boolean = false, basemapLoaded: (Boolean) -> Unit) {
    val colors = MaterialTheme.colorScheme
    val dark = colors.background.luminance() < .5f
    val routeColor = "#%06X".format(colors.primary.toArgb() and 0xFFFFFF)
    val pickupColor = "#%06X".format(colors.tertiary.toArgb() and 0xFFFFFF)
    val casingColor = "#%06X".format(colors.surface.toArgb() and 0xFFFFFF)
    val canvasColor = "#%06X".format(colors.background.toArgb() and 0xFFFFFF)
    val positionColor = "#%06X".format(driverPositionColor().toArgb() and 0xFFFFFF)
    var frameRequest by remember { mutableIntStateOf(0) }
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val view = remember(context, dark) {
        MapLibre.getInstance(context)
        object : MapView(context) {
            override fun dispatchTouchEvent(event: MotionEvent): Boolean {
                if (event.actionMasked == MotionEvent.ACTION_DOWN) parent?.requestDisallowInterceptTouchEvent(true)
                return super.dispatchTouchEvent(event)
            }
        }.apply { onCreate(null) }
    }
    var readyMap by remember(view) { mutableStateOf<MapLibreMap?>(null) }
    var readyStyle by remember(view) { mutableStateOf<Style?>(null) }
    DisposableEffect(view, lifecycle, routeColor, pickupColor, casingColor) {
        var started = false
        var resumed = false
        var disposed = false
        var fallback = false
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                Lifecycle.Event.ON_START -> { view.onStart(); started = true }
                Lifecycle.Event.ON_RESUME -> { view.onResume(); resumed = true }
                Lifecycle.Event.ON_PAUSE -> { view.onPause(); resumed = false }
                Lifecycle.Event.ON_STOP -> { view.onStop(); started = false }
                else -> Unit
            }
        }
        lifecycle.addObserver(observer)
        view.getMapAsync { map ->
            if (disposed) return@getMapAsync
            readyMap = map
            fun loadStyle(builder: Style.Builder) {
                map.setStyle(builder) { style ->
                    if (disposed) return@setStyle
                    style.addSource(GeoJsonSource(ROUTE_SOURCE, FeatureCollection.fromFeatures(emptyList())))
                    style.addLayer(LineLayer("dalan-route-casing", ROUTE_SOURCE).withProperties(
                        PropertyFactory.lineColor(casingColor), PropertyFactory.lineWidth(8f)))
                    style.addLayer(LineLayer("dalan-route-line", ROUTE_SOURCE).withProperties(
                        PropertyFactory.lineColor(routeColor), PropertyFactory.lineWidth(5f)))
                    addStop(style, "dalan-pickup", null, pickupColor)
                    addStop(style, "dalan-dropoff", null, routeColor)
                    readyStyle = style
                    basemapLoaded(!fallback)
                }
            }
            view.addOnDidFailLoadingMapListener {
                if (!disposed && !fallback) {
                    fallback = true
                    view.post { if (!disposed) loadStyle(Style.Builder().fromJson(EMPTY_STYLE.replace("#F1EEE8", canvasColor))) }
                }
            }
            loadStyle(Style.Builder().fromUri(BASEMAP + if (dark) "dark" else "liberty"))
        }
        onDispose {
            disposed = true
            lifecycle.removeObserver(observer)
            if (resumed) view.onPause()
            if (started) view.onStop()
            view.onDestroy()
        }
    }

    LaunchedEffect(view, readyMap, readyStyle, lines, stops, currentPosition, frameRequest) {
        val map = readyMap ?: return@LaunchedEffect
        val style = readyStyle ?: return@LaunchedEffect
        val source = style.getSourceAs<GeoJsonSource>(ROUTE_SOURCE) ?: return@LaunchedEffect
        source.setGeoJson(FeatureCollection.fromFeatures(lines.map { Feature.fromGeometry(it) }))
        for ((kind, id) in listOf("Pickup" to "dalan-pickup", "Drop-off" to "dalan-dropoff")) {
            style.getSourceAs<GeoJsonSource>(id)?.setGeoJson(FeatureCollection.fromFeatures(stops.filter { it.stopKind == kind }
                .map { Feature.fromGeometry(Point.fromLngLat(it.destination.longitude, it.destination.latitude)) }))
        }
        val current = style.getSourceAs<GeoJsonSource>("dalan-current-position")
        if (current == null) {
            style.addSource(GeoJsonSource("dalan-current-position", FeatureCollection.fromFeatures(emptyList())))
            style.addLayer(CircleLayer("dalan-current-position", "dalan-current-position").withProperties(
                PropertyFactory.circleColor(positionColor), PropertyFactory.circleRadius(8f),
                PropertyFactory.circleStrokeColor("#FFFFFF"), PropertyFactory.circleStrokeWidth(3f)))
        }
        style.getSourceAs<GeoJsonSource>("dalan-current-position")?.let {
            if (currentPosition == null) it.setGeoJson(FeatureCollection.fromFeatures(emptyList())) else it.setGeoJson(currentPosition)
        }
        val points = (stops.map { Point.fromLngLat(it.destination.longitude, it.destination.latitude) } + listOfNotNull(currentPosition) + lines.flatMap { it.coordinates() })
            .map { LatLng(it.latitude(), it.longitude()) }.distinct()
        view.post {
            if (!view.isAttachedToWindow) return@post
            val update = if (points.size == 1) CameraUpdateFactory.newLatLngZoom(points.first(), 16.0)
                else CameraUpdateFactory.newLatLngBounds(LatLngBounds.Builder().includes(points).build(), 72)
            map.moveCamera(update)
        }
    }
    Box(modifier) {
        key(view) { AndroidView(factory = { view }, modifier = Modifier.fillMaxSize().semantics {
            contentDescription = "Delivery route map. Use the stop details for accessible addresses."
            stateDescription = if (focused) "Map active. Tap outside to scroll." else "Page scrolling available."
        }) }
        FilledTonalIconButton(onClick = { frameRequest++ }, modifier = Modifier.align(Alignment.BottomEnd).padding(end = 12.dp, bottom = 48.dp).size(48.dp)) {
            Box(Modifier.semantics { contentDescription = "Fit route to map" }) { DriverWorkspaceIcon("Recenter") }
        }
    }
}

private fun addStop(style: Style, id: String, point: Point?, color: String) {
    style.addSource(GeoJsonSource(id, FeatureCollection.fromFeatures(emptyList())))
    if (point != null) style.getSourceAs<GeoJsonSource>(id)?.setGeoJson(point)
    style.addLayer(CircleLayer(id, id).withProperties(PropertyFactory.circleColor(color),
        PropertyFactory.circleRadius(9f), PropertyFactory.circleStrokeColor("#FFFFFF"),
        PropertyFactory.circleStrokeWidth(3f)))
}
