package com.example.dalanmobile.dto

import kotlinx.serialization.Serializable

@Serializable
data class TokenResponseDTO(
    val accessToken: String,
    val tokenType: String,
    val expiresIn: Int,
    val refreshToken: String? = null
)