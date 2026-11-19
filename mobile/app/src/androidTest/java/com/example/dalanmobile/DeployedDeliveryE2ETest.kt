package com.example.dalanmobile

import android.os.Bundle
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.DriverApi
import com.example.dalanmobile.driver.SessionStore
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith

/** Opt-in verification against the deployed server, using the installed app's current session. */
@RunWith(AndroidJUnit4::class)
class DeployedDeliveryE2ETest {
    @Test fun current_driver_session_can_read_deployed_tasks(): Unit = runBlocking {
        val arguments = InstrumentationRegistry.getArguments()
        assumeTrue(arguments.getString("dalanDeliverySessionProbe") == "true")
        assertEquals("https://dalan.lol/", BuildConfig.API_BASE_URL)
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val session = SessionStore(instrumentation.targetContext).load()
        assertNotNull("Sign in as Driver on the installed phone app before running this check", session)
        DriverApi().use { api ->
            val tasks = api.list(session!!)
            instrumentation.sendStatus(0, Bundle().apply {
                putString("dalanDriverId", session.driverId)
                putString("dalanActiveTaskCount", tasks.size.toString())
            })
        }
    }
}
