package com.example.dalanmobile

import androidx.room.Room
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.DriverDatabase
import com.example.dalanmobile.driver.PendingEvent
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class DriverQueueTest {
    @Test
    fun queueSurvivesRestartInOrderAndRemainsScopedToDriver() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val name = "queue-test-${UUID.randomUUID()}.db"
        var database = Room.databaseBuilder(context, DriverDatabase::class.java, name).build()
        try {
            val initial = database.driverDao()
            initial.enqueue(event("first", "Pickup"))
            initial.enqueue(event("second", "Pickup"))
            initial.enqueue(event("first", "Deliver"))
            database.close()

            database = Room.databaseBuilder(context, DriverDatabase::class.java, name).build()
            val dao = database.driverDao()
            val updates = dao.queued("first")
            assertEquals(listOf("Pickup", "Deliver"), updates.map { it.kind })
            dao.fail(updates.first().sequence, "Assignment changed")
            assertEquals("Assignment changed", dao.queued("first").first().failure)
            dao.discard("first", "delivery", "assignment")
            assertTrue(dao.queued("first").isEmpty())
            assertEquals("Pickup", dao.queued("second").single().kind)
        } finally {
            database.close()
            context.deleteDatabase(name)
        }
        Unit
    }

    private fun event(owner: String, kind: String) = PendingEvent(owner = owner, deliveryId = "delivery",
        assignment = "assignment", kind = kind, payload = "{}")
}
