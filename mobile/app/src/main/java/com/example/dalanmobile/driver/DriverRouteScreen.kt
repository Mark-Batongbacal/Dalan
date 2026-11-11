package com.example.dalanmobile.driver

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
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
    LaunchedEffect(task.id, task.assignedAtUtc, attempt, pickup, dropoff) {
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
        Column(Modifier.fillMaxSize().padding(padding).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)) {
            TextButton(onClick = close) { Text("Back to delivery") }
            Text("Pickup to drop-off", style = MaterialTheme.typography.titleLarge)
            Text(task.recipientName)
            route?.let {
                val distance = NumberFormat.getNumberInstance().apply { maximumFractionDigits = 1 }
                    .format(it.distanceMeters / 1000)
                val minutes = (it.durationSeconds / 60).roundToInt().coerceAtLeast(1)
                Text("$distance km · about $minutes min by road")
            }
            if (status.isNotEmpty()) Text(status, Modifier.semantics { liveRegion = LiveRegionMode.Polite })
            if (pickup != null && dropoff != null) {
                key(task.id, attempt) {
                    DeliveryMap(pickup, dropoff, line, Modifier.fillMaxWidth().weight(1f)) { mapLoaded = it }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    MapStopLegend("Pickup", Color(0xFF23664C))
                    MapStopLegend("Drop-off", Color(0xFF315C8C))
                }
                if (mapLoaded == false) Text("Basemap unavailable. Showing delivery geometry.")
                else Text("© OpenStreetMap contributors · OpenFreeMap",
                    style = MaterialTheme.typography.labelMedium)
                if ((status.isNotEmpty() && status != "Loading road route…") || mapLoaded == false) {
                    TextButton(onClick = { mapLoaded = null; attempt++ }) { Text("Retry map") }
                }
            }
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
private fun DeliveryMap(pickup: Point, dropoff: Point, line: LineString?, modifier: Modifier,
                        basemapLoaded: (Boolean) -> Unit) {
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
                        PropertyFactory.lineColor("#87501E"), PropertyFactory.lineWidth(5f)))
                    addStop(style, "dalan-pickup", pickup, "#23664C")
                    addStop(style, "dalan-dropoff", dropoff, "#315C8C")
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

    LaunchedEffect(view, readyMap, readyStyle, line, pickup, dropoff) {
        val map = readyMap ?: return@LaunchedEffect
        val style = readyStyle ?: return@LaunchedEffect
        val source = style.getSourceAs<GeoJsonSource>(ROUTE_SOURCE) ?: return@LaunchedEffect
        if (line == null) source.setGeoJson(FeatureCollection.fromFeatures(emptyList()))
        else source.setGeoJson(line)
        val points = (listOf(pickup, dropoff) + line?.coordinates().orEmpty())
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

private fun addStop(style: Style, id: String, point: Point, color: String) {
    style.addSource(GeoJsonSource(id, point))
    style.addLayer(CircleLayer(id, id).withProperties(PropertyFactory.circleColor(color),
        PropertyFactory.circleRadius(9f), PropertyFactory.circleStrokeColor("#FFFFFF"),
        PropertyFactory.circleStrokeWidth(3f)))
}
