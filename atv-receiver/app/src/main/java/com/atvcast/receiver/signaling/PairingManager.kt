package com.atvcast.receiver.signaling

import kotlin.random.Random

class PairingManager {
    private var currentCode: String = generateCode()

    fun generateCode(): String {
        currentCode = String.format("%04d", Random.nextInt(10000))
        return currentCode
    }

    fun validateCode(input: String): Boolean = input == currentCode

    fun getCurrentCode(): String = currentCode

    fun resetAfterPairing() {
        generateCode()
    }
}
