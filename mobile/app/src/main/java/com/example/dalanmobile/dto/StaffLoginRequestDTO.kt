package com.example.dalanmobile.dto

import kotlinx.serialization.Serializable

@Serializable
data class StaffLoginRequestDTO(
    val email: String,
    val password: String
)