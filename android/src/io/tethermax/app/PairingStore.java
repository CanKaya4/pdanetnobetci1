package io.tethermax.app;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.security.KeyStore;
import java.security.SecureRandom;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Fail-closed encrypted storage. Never export the Android Keystore wrapping key. */
final class PairingStore {
    private static final String ALIAS = "io.tethermax.pairing.wrap.v1";
    private PairingStore() {}
    static synchronized String get(Context context) throws Exception {
        SharedPreferences prefs = context.getSharedPreferences("pairing", Context.MODE_PRIVATE);
        KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null);
        if (!store.containsAlias(ALIAS)) {
            if (prefs.contains("encrypted")) throw new IllegalStateException("Pairing key unavailable");
            KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());
            generator.generateKey();
        }
        SecretKey key = (SecretKey)store.getKey(ALIAS, null);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        String encrypted = prefs.getString("encrypted", null);
        if (encrypted != null) {
            byte[] packed = Base64.decode(encrypted, Base64.NO_WRAP);
            if (packed.length != 60) throw new IllegalStateException("Invalid encrypted pairing record");
            cipher.init(Cipher.DECRYPT_MODE, key, new GCMParameterSpec(128, packed, 0, 12));
            cipher.updateAAD(ALIAS.getBytes(java.nio.charset.StandardCharsets.UTF_8));
            String value = new String(cipher.doFinal(packed, 12, packed.length - 12), java.nio.charset.StandardCharsets.US_ASCII);
            if (!value.matches("[a-f0-9]{32}")) throw new IllegalStateException("Invalid pairing record");
            return value;
        }
        String value = prefs.getString("code", null);
        if (value != null && !value.matches("[a-f0-9]{32}")) throw new IllegalStateException("Invalid legacy pairing record");
        if (value == null) {
            byte[] random = new byte[16]; new SecureRandom().nextBytes(random);
            StringBuilder hex = new StringBuilder();
            for (byte b : random) hex.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
            value = hex.toString();
        }
        cipher.init(Cipher.ENCRYPT_MODE, key);
        cipher.updateAAD(ALIAS.getBytes(java.nio.charset.StandardCharsets.UTF_8));
        byte[] ciphertext = cipher.doFinal(value.getBytes(java.nio.charset.StandardCharsets.US_ASCII));
        byte[] packed = new byte[12 + ciphertext.length];
        System.arraycopy(cipher.getIV(), 0, packed, 0, 12);
        System.arraycopy(ciphertext, 0, packed, 12, ciphertext.length);
        if (!prefs.edit().putString("encrypted", Base64.encodeToString(packed, Base64.NO_WRAP)).remove("code").commit())
            throw new IllegalStateException("Pairing record could not be saved");
        return value;
    }
}
