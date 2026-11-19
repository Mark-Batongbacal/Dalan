package com.example.dalanmobile.driver

import androidx.compose.foundation.background
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.collectAsState
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import android.Manifest
import android.content.pm.PackageManager
import android.os.SystemClock
import kotlinx.coroutines.delay
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
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
import org.maplibre.geojson.LineString
import org.maplibre.geojson.Point
import kotlin.math.roundToInt

private const val BASEMAP = "https://tiles.openfreemap.org/styles/liberty"
private const val EMPTY_STYLE = """{"version":8,"sources":{},"layers":[{"id":"background","type":"background","paint":{"background-color":"#F1EEE8"}}]}"""
private const val ROUTE_SOURCE = "dalan-route"

@Composable
fun DriverRouteScreen(task: TaskInfo, model: DriverViewModel, close: () -> Unit) {
    val pickup = stopPoint(task.pickupLatitude, task.pickupLongitude)
    val dropoff = stopPoint(task.dropoffLatitude, task.dropoffLongitude)
    var route by remember(task.id) { mutableStateOf<DriverRoadRoute?>(null) }
    var line by remember(task.id) { mutableStateOf<LineString?>(null) }
    var status by remember(task.id) { mutableStateOf("Loading road route…") }
    var mapLoaded by remember(task.id) { mutableStateOf<Boolean?>(null) }
    var attempt by remember(task.id) { mutableIntStateOf(0) }
    val context = LocalContext.current
    val workspace by model.state.collectAsState()
    val navigationLifecycle = LocalLifecycleOwner.current.lifecycle
    val nextStopKey = workspace.tasks.sortedWith(compareBy<TaskInfo> { it.assignedAtUtc }.thenBy {
        if (it.status == "PickedUp") it.dropoffPosition ?: Int.MAX_VALUE else it.pickupPosition ?: Int.MAX_VALUE
    }).firstOrNull()?.let { "${it.id}:${it.status}:${it.assignedAtUtc}" }
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
    LaunchedEffect(navigating, nextStopKey, attempt) {
        if (!navigating) return@LaunchedEffect
        navigation = null; line = null; route = null; guidance = ""; lastRequest = 0L
        while (true) {
            if (!navigationLifecycle.currentState.isAtLeast(Lifecycle.State.RESUMED)) { delay(15_000); continue }
            try {
                val fix = currentLocation(context)
                if (fix == null || !fix.hasAccuracy() || fix.accuracy > 50 ||
                    java.time.Instant.now().toEpochMilli() - fix.time !in 0..120_000) {
                    status = "Navigation paused: allow precise location and capture a fresh accurate position. Last route, if shown, is only a preview."
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
                        val geometry = LineString.fromPolyline(result.route.encodedPolyline6, 6)
                        require(geometry.coordinates().size >= 2)
                        navigation = result; route = result.route; line = geometry; shapeProgress = 0
                    }
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
                    status = "Current-position guidance. Route ETA is an estimate; follow road signs and conditions."
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                guidance = ""; position = null
                status = when ((error as? DriverHttpException)?.status) {
                    401,403 -> "Sign in again to navigate."
                    422 -> "No route or fresh accurate position. Check location and retry."
                    429 -> "Routing request limit reached. Wait before retrying."
                    else -> "Navigation unavailable. Last geometry is a preview; offline road guidance is not available."
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
                else -> "Road route unavailable. Check your connection or retry."
            }
        }
    }

    Scaffold { padding ->
        Column(Modifier.fillMaxSize().padding(padding).verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)) {
            TextButton(onClick = close, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back to delivery") }
            Text(if (navigating) "Navigate to next assigned stop" else "Pickup to drop-off", Modifier.semantics { heading() }, style = MaterialTheme.typography.titleLarge)
            Text(if (navigating) navigation?.let { active -> workspace.tasks.firstOrNull { it.id == active.deliveryId }?.recipientName } ?: "Next assigned stop" else task.recipientName)
            if (pickup != null && dropoff != null) {
                key(task.id, attempt) {
                    DeliveryMap(if (navigating) null else pickup,
                        navigation?.destination?.let { Point.fromLngLat(it.longitude, it.latitude) } ?: dropoff,
                        line, Modifier.fillMaxWidth().height(360.dp).clip(MaterialTheme.shapes.large), position) { mapLoaded = it }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    MapStopLegend(if (navigating) "Current position" else "Pickup", if (navigating) Color(0xFF315C8C) else Color(0xFF23664C))
                    MapStopLegend(if (navigating) "Next stop" else "Drop-off", Color(0xFFB94E12))
                }
                if (mapLoaded == false) Text("Basemap unavailable. Showing delivery geometry.")
                else Text("© OpenStreetMap contributors · OpenFreeMap",
                    style = MaterialTheme.typography.labelMedium)
                if ((status.isNotEmpty() && status != "Loading road route…") || mapLoaded == false) {
                    TextButton(onClick = { mapLoaded = null; attempt++ }, modifier = Modifier.heightIn(min = 48.dp)) { Text("Retry map") }
                }
            }
            Text(if (navigating) "Navigation follows confirmed dispatch order. Location is used while this map is open; separate location sharing is optional."
                else "Road estimate between assigned stops. Start navigation to route from your current position.")
            TextButton(onClick = {
                if (navigating) { navigating = false; navigation = null; position = null; guidance = "" }
                else if (ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED) navigating = true
                else permission.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
            }, modifier = Modifier.heightIn(min = 48.dp)) { Text(if (navigating) "Stop navigation" else "Navigate from current location") }
            navigation?.let { Text("${it.stopKind}: ${it.address}") }
            if (navigating && guidance.isNotBlank() && remainingMeters != null) {
                Text("Approximately ${(remainingMeters!! / 1000 * 10).roundToInt() / 10.0} km remaining · ${((remainingSeconds ?: 0.0) / 60).roundToInt().coerceAtLeast(1)} min estimated")
            }
            if (guidance.isNotBlank()) Text(guidance, Modifier.semantics { liveRegion = LiveRegionMode.Polite }, style = MaterialTheme.typography.titleMedium)
            route?.let {
                val distance = NumberFormat.getNumberInstance().apply { maximumFractionDigits = 1 }
                    .format(it.distanceMeters / 1000)
                val minutes = (it.durationSeconds / 60).roundToInt().coerceAtLeast(1)
                Text("$distance km · about $minutes min by road")
            }
            if (status.isNotEmpty()) Text(status, Modifier.semantics { liveRegion = LiveRegionMode.Polite })

        }
    }
}

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
private fun DeliveryMap(pickup: Point?, dropoff: Point, line: LineString?, modifier: Modifier,
                        currentPosition: Point? = null, basemapLoaded: (Boolean) -> Unit) {
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val view = remember(context) {
        MapLibre.getInstance(context)
        MapView(context).apply { onCreate(null) }
    }
    var readyMap by remember(view) { mutableStateOf<MapLibreMap?>(null) }
    var readyStyle by remember(view) { mutableStateOf<Style?>(null) }
    DisposableEffect(view, lifecycle) {
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
                        PropertyFactory.lineColor("#FFFFFF"), PropertyFactory.lineWidth(8f)))
                    style.addLayer(LineLayer("dalan-route-line", ROUTE_SOURCE).withProperties(
                        PropertyFactory.lineColor("#B94E12"), PropertyFactory.lineWidth(5f)))
                    addStop(style, "dalan-pickup", pickup, "#23664C")
                    addStop(style, "dalan-dropoff", dropoff, "#B94E12")
                    readyStyle = style
                    basemapLoaded(!fallback)
                }
            }
            view.addOnDidFailLoadingMapListener {
                if (!disposed && !fallback) {
                    fallback = true
                    view.post { if (!disposed) loadStyle(Style.Builder().fromJson(EMPTY_STYLE)) }
                }
            }
            loadStyle(Style.Builder().fromUri(BASEMAP))
        }
        onDispose {
            disposed = true
            lifecycle.removeObserver(observer)
            if (resumed) view.onPause()
            if (started) view.onStop()
            view.onDestroy()
        }
    }

    LaunchedEffect(view, readyMap, readyStyle, line, pickup, dropoff, currentPosition) {
        val map = readyMap ?: return@LaunchedEffect
        val style = readyStyle ?: return@LaunchedEffect
        val source = style.getSourceAs<GeoJsonSource>(ROUTE_SOURCE) ?: return@LaunchedEffect
        if (line == null) source.setGeoJson(FeatureCollection.fromFeatures(emptyList()))
        else source.setGeoJson(line)
        style.getSourceAs<GeoJsonSource>("dalan-pickup")?.let {
            if (pickup == null) it.setGeoJson(FeatureCollection.fromFeatures(emptyList())) else it.setGeoJson(pickup)
        }
        style.getSourceAs<GeoJsonSource>("dalan-dropoff")?.setGeoJson(dropoff)
        val current = style.getSourceAs<GeoJsonSource>("dalan-current-position")
        if (current == null) {
            style.addSource(GeoJsonSource("dalan-current-position", FeatureCollection.fromFeatures(emptyList())))
            style.addLayer(CircleLayer("dalan-current-position", "dalan-current-position").withProperties(
                PropertyFactory.circleColor("#315C8C"), PropertyFactory.circleRadius(8f),
                PropertyFactory.circleStrokeColor("#FFFFFF"), PropertyFactory.circleStrokeWidth(3f)))
        }
        style.getSourceAs<GeoJsonSource>("dalan-current-position")?.let {
            if (currentPosition == null) it.setGeoJson(FeatureCollection.fromFeatures(emptyList())) else it.setGeoJson(currentPosition)
        }
        val points = (listOfNotNull(pickup, dropoff, currentPosition) + line?.coordinates().orEmpty())
            .map { LatLng(it.latitude(), it.longitude()) }.distinct()
        view.post {
            if (!view.isAttachedToWindow) return@post
            val update = if (points.size == 1) CameraUpdateFactory.newLatLngZoom(points.first(), 16.0)
                else CameraUpdateFactory.newLatLngBounds(LatLngBounds.Builder().includes(points).build(), 72)
            map.moveCamera(update)
        }
    }
    AndroidView(factory = { view }, modifier = modifier.semantics { liveRegion = LiveRegionMode.Polite })
}

private fun addStop(style: Style, id: String, point: Point?, color: String) {
    style.addSource(GeoJsonSource(id, FeatureCollection.fromFeatures(emptyList())))
    if (point != null) style.getSourceAs<GeoJsonSource>(id)?.setGeoJson(point)
    style.addLayer(CircleLayer(id, id).withProperties(PropertyFactory.circleColor(color),
        PropertyFactory.circleRadius(9f), PropertyFactory.circleStrokeColor("#FFFFFF"),
        PropertyFactory.circleStrokeWidth(3f)))
}
