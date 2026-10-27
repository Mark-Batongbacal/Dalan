package com.example.dalanmobile.dto

import kotlinx.serialization.Serializable

@Serializable
data class StaffProfileResponseDTO(
    val id: String,
    val email: String,
    val roles: List<String>
)