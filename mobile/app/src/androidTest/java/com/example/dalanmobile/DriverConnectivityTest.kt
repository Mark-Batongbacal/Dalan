package com.example.dalanmobile

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.DriverApi
import com.example.dalanmobile.driver.DriverHttpException
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.fail
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith

/** Explicit opt-in only; probes transport with a nonexistent account, without touching saved sessions. */
@RunWith(AndroidJUnit4::class)
class DriverConnectivityTest {
    @Test fun configured_endpoint_is_reachable_from_the_installed_app(): Unit = runBlocking {
        val arguments = InstrumentationRegistry.getArguments()
        assumeTrue(arguments.getString("dalanConnectivityProbe") == "true")
        assertEquals(arguments.getString("dalanExpectedApiUrl"), BuildConfig.API_BASE_URL)
        assertEquals(BuildConfig.APPLICATION_ID, InstrumentationRegistry.getInstrumentation().targetContext.packageName)
        DriverApi().use { api ->
            try {
                api.login("connectivity-${UUID.randomUUID()}@example.invalid", "Connectivity-only#1234")
                fail("A nonexistent probe account must not authenticate")
            } catch (error: DriverHttpException) {
                assertEquals("The API must reject the probe credentials, rather than fail DNS/TLS/transport", 401, error.status)
            }
        }
    }
}
