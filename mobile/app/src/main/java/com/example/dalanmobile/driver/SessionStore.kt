package com.example.dalanmobile.driver

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import kotlinx.serialization.encodeToString

class SessionStore(context: Context) {
    private val preferences = context.getSharedPreferences("driver-session", Context.MODE_PRIVATE)
    private val keyName = "dalan-driver-session"

    fun save(session: DriverSession) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val bytes = cipher.doFinal(driverJson.encodeToString(session).encodeToByteArray())
        preferences.edit().putString("session", Base64.encodeToString(cipher.iv + bytes, Base64.NO_WRAP)).apply()
    }

    fun load(): DriverSession? = runCatching {
        val encoded = preferences.getString("session", null) ?: return null
        val bytes = Base64.decode(encoded, Base64.NO_WRAP)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, bytes.copyOfRange(0, 12)))
        driverJson.decodeFromString<DriverSession>(cipher.doFinal(bytes, 12, bytes.size - 12).decodeToString())
    }.getOrNull()

    fun clear() {
        preferences.edit().remove("session").apply()
    }

    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(keyName, null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").run {
            init(KeyGenParameterSpec.Builder(
                keyName, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
            ).setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build())
            generateKey()
        }
    }
}
