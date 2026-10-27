package com.example.dalanmobile.dto

import kotlinx.serialization.Serializable

@Serializable
data class VehicleDTO(
    val id: String,
    val registrationNumber: String
)