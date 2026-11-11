package com.example.dalanmobile.driver

import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.PrimaryKey
import androidx.room.Query
import androidx.room.RoomDatabase
import kotlinx.coroutines.flow.Flow

@Entity(primaryKeys = ["owner", "id"])
data class SavedTask(val owner: String, val id: String, val payload: String)

@Entity
data class PendingEvent(
    @PrimaryKey(autoGenerate = true) val sequence: Long = 0,
    val owner: String,
    val deliveryId: String,
    val assignment: String,
    val kind: String,
    val payload: String,
    val failure: String? = null,
)

@Dao
interface DriverDao {
    @Query("SELECT * FROM SavedTask WHERE owner = :owner")
    fun tasks(owner: String): Flow<List<SavedTask>>

    @Query("SELECT * FROM SavedTask WHERE owner = :owner AND id = :id")
    suspend fun task(owner: String, id: String): SavedTask?

    @Query("DELETE FROM SavedTask WHERE owner = :owner")
    suspend fun clearTasks(owner: String)

    @Query("DELETE FROM SavedTask WHERE owner = :owner AND id = :id")
    suspend fun removeTask(owner: String, id: String)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun saveTasks(tasks: List<SavedTask>)

    @Query("SELECT * FROM PendingEvent WHERE owner = :owner ORDER BY sequence")
    fun events(owner: String): Flow<List<PendingEvent>>

    @Query("SELECT * FROM PendingEvent WHERE owner = :owner ORDER BY sequence")
    suspend fun queued(owner: String): List<PendingEvent>

    @Insert
    suspend fun enqueue(event: PendingEvent)

    @Query("DELETE FROM PendingEvent WHERE sequence = :sequence")
    suspend fun removeEvent(sequence: Long)

    @Query("UPDATE PendingEvent SET failure = :reason WHERE sequence = :sequence")
    suspend fun fail(sequence: Long, reason: String)

    @Query("DELETE FROM PendingEvent WHERE owner = :owner AND deliveryId = :id AND assignment = :assignment")
    suspend fun discard(owner: String, id: String, assignment: String)
}

@Database(entities = [SavedTask::class, PendingEvent::class], version = 1, exportSchema = true)
abstract class DriverDatabase : RoomDatabase() {
    abstract fun driverDao(): DriverDao
}
