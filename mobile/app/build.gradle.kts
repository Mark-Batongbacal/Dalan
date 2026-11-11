plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.ksp)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
}

val driverApiUrl = providers.gradleProperty("dalanDriverApiUrl")
val debugDriverApiUrl = providers.gradleProperty("dalanDriverDebugApiUrl")
val isolatedE2e = providers.gradleProperty("dalanE2E").map(String::toBoolean).getOrElse(false)

fun buildConfigUrl(url: String): String {
    require('"' !in url && '\\' !in url && '\n' !in url && '\r' !in url)
    return "\"$url\""
}

android {
    namespace = "com.example.dalanmobile"
    compileSdk {
        version = release(37)
    }

    defaultConfig {
        applicationId = "com.example.dalanmobile"
        minSdk = 29
        targetSdk = 37
        versionCode = 1
        versionName = "1.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildTypes {
        debug {
            if (isolatedE2e) applicationIdSuffix = ".e2e"
            buildConfigField(
                "String",
                "API_BASE_URL",
                buildConfigUrl(debugDriverApiUrl.orElse(driverApiUrl).getOrElse("http://10.0.2.2:5072/"))
            )
        }

        release {
            optimization {
                enable = false
            }
            buildConfigField(
                "String",
                "API_BASE_URL",
                buildConfigUrl(driverApiUrl.getOrElse("https://replace-before-release.invalid/").also {
                    require(it.startsWith("https://")) { "dalanDriverApiUrl must use HTTPS for release builds." }
                })
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }
}

dependencies {
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.room.runtime)
    implementation(libs.maplibre.android)
    ksp(libs.room.compiler)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    testImplementation(libs.junit)
    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    androidTestImplementation(libs.androidx.espresso.core)
    androidTestImplementation(libs.androidx.junit)
    debugImplementation(libs.androidx.compose.ui.test.manifest)
    debugImplementation(libs.androidx.compose.ui.tooling)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.ktor.client.core)
    implementation(libs.ktor.client.android)
    implementation(libs.ktor.client.content.negotiation)
    implementation(libs.ktor.serialization.kotlinx.json)
}
ksp {
    arg("room.schemaLocation", file("schemas").path)
}
