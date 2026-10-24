package io.tethermax.app;

import android.content.Context;
import android.net.LocalServerSocket;
import android.net.LocalSocket;
import android.os.Build;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import java.io.*;
import java.security.*;
import java.security.spec.*;
import java.util.*;
import java.util.concurrent.*;
import javax.crypto.*;
import javax.crypto.spec.*;

/** USB-only committed ECDH pairing; short code is a transient SAS, never a password. */
final class UsbPairing implements AutoCloseable {
    private static final String ALIAS = "io.tethermax.identity.ecdh.v2";
    static volatile String pendingCode;
    private static volatile CompletableFuture<Boolean> pending;
    static void confirm(boolean yes) { CompletableFuture<Boolean> p = pending; if (p != null) p.complete(yes); }
    static void confirm(String expected, boolean yes) { if (expected.equals(pendingCode)) confirm(yes); }
    private final Context context;
    private final LocalServerSocket listener;
    private volatile LocalSocket active;
    private volatile boolean closed;
    private final Thread worker;
    private int attempts;
    private long window;
    UsbPairing(Context context) throws IOException {
        this.context = context.getApplicationContext();
        listener = new LocalServerSocket("io.tethermax.pair.v2");
        worker = new Thread(() -> {
            while (!closed) {
                try (LocalSocket socket = listener.accept()) {
                    synchronized (this) { if (closed) break; active = socket; }
                    socket.setSoTimeout(90000);
                    long now = android.os.SystemClock.elapsedRealtime();
                    if (now - window > 60000) { window = now; attempts = 0; }
                    exchange(socket);
                } catch (Exception e) { /* Fail closed; never log identities, SAS or secrets. */ }
                finally { active = null; pendingCode = null; pending = null; }
            }
        }, "tethermax-pairing"); worker.start();
    }
    private static byte[] hash(byte[] value) throws Exception { return MessageDigest.getInstance("SHA-256").digest(value); }
    private static byte[] concat(byte[]... values) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream(); for (byte[] value : values) out.write(value); return out.toByteArray();
    }
    private static byte[] mac(byte[] key, byte[] value) throws Exception {
        Mac hmac = Mac.getInstance("HmacSHA256"); hmac.init(new SecretKeySpec(key, "HmacSHA256")); return hmac.doFinal(value);
    }
    private static byte[] read(DataInputStream in, int count) throws IOException { byte[] b = new byte[count]; in.readFully(b); return b; }
    private static byte[] readPublic(DataInputStream in) throws IOException {
        int size = in.readUnsignedShort(); if (size != 91) throw new IOException("Invalid P256 SPKI length"); return read(in, size);
    }
    private static void writePublic(DataOutputStream out, byte[] pub, byte[] nonce) throws IOException { out.writeShort(pub.length); out.write(pub); out.write(nonce); out.flush(); }
    private static byte[] crypt(int mode, byte[] key, byte[] nonce, byte[] aad, byte[] value) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(mode, new SecretKeySpec(key,"AES"), new GCMParameterSpec(128,nonce)); cipher.updateAAD(aad); return cipher.doFinal(value);
    }
    private void exchange(LocalSocket socket) throws Exception {
        if (Build.VERSION.SDK_INT < 31) throw new IOException("Secure pairing requires Android 12+");
        DataInputStream in = new DataInputStream(socket.getInputStream()); DataOutputStream out = new DataOutputStream(socket.getOutputStream());
        if (in.readInt() != 0x54504d32) return;
        KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null);
        if (!store.containsAlias(ALIAS)) {
            KeyPairGenerator gen = KeyPairGenerator.getInstance("EC", "AndroidKeyStore");
            gen.initialize(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_AGREE_KEY).setAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1")).build()); gen.generateKeyPair();
        }
        PrivateKey own = (PrivateKey)store.getKey(ALIAS,null); byte[] pub = store.getCertificate(ALIAS).getPublicKey().getEncoded();
        byte[] nonce = new byte[32]; new SecureRandom().nextBytes(nonce);
        byte[] remoteCommit = read(in,32); out.write(hash(concat(pub,nonce))); out.flush();
        byte[] remotePub = readPublic(in), remoteNonce = read(in,32);
        if (!MessageDigest.isEqual(remoteCommit,hash(concat(remotePub,remoteNonce)))) throw new IOException("Commitment mismatch");
        writePublic(out,pub,nonce);
        PublicKey peer = KeyFactory.getInstance("EC").generatePublic(new X509EncodedKeySpec(remotePub));
        java.security.interfaces.ECPublicKey ec = (java.security.interfaces.ECPublicKey)peer;
        if (ec.getParams().getCurve().getField().getFieldSize() != 256) throw new IOException("Unsupported curve");
        KeyAgreement agreement = KeyAgreement.getInstance("ECDH","AndroidKeyStore"); agreement.init(own); agreement.doPhase(peer,true);
        byte[] transcript = hash(concat("TetherMax-Pair-v2".getBytes("US-ASCII"),remotePub,remoteNonce,pub,nonce));
        byte[] prk = mac(transcript,agreement.generateSecret());
        byte[] key = mac(prk,concat("session".getBytes("US-ASCII"),new byte[]{1}));
        byte[] sas = mac(key,"sas".getBytes("US-ASCII"));
        long number = ((sas[0]&255L)<<24)|((sas[1]&255L)<<16)|((sas[2]&255L)<<8)|(sas[3]&255L);
        String id = android.util.Base64.encodeToString(hash(remotePub),android.util.Base64.NO_WRAP);
        android.content.SharedPreferences trust = context.getSharedPreferences("trusted-v2",Context.MODE_PRIVATE);
        boolean known = trust.getBoolean(id,false);
        if (!known && ++attempts > 3) throw new IOException("Pairing cooldown");
        CompletableFuture<Boolean> approval = new CompletableFuture<>();
        if (!known) { pending = approval; pendingCode = String.format(Locale.ROOT,"%06d",number%1000000); }
        // Client must confirm its screen before the phone can release the transport secret.
        byte[] confirmNonce = read(in,12), confirmCipher = read(in,17);
        if (!Arrays.equals(new byte[]{1},crypt(Cipher.DECRYPT_MODE,key,confirmNonce,transcript,confirmCipher))) return;
        if (!known && !approval.get(75,TimeUnit.SECONDS)) return;
        if (closed) return;
        byte[] token = PairingStore.get(context).getBytes("US-ASCII");
        byte[] responseNonce = new byte[12]; new SecureRandom().nextBytes(responseNonce);
        byte[] encrypted = crypt(Cipher.ENCRYPT_MODE,key,responseNonce,transcript,token);
        if (!known && !trust.edit().putBoolean(id,true).commit()) throw new IOException("Trust save failed");
        out.write(responseNonce); out.write(encrypted); out.flush();
        Arrays.fill(key,(byte)0); Arrays.fill(prk,(byte)0); Arrays.fill(token,(byte)0);
    }
    static void forget(Context context) {
        confirm(false); context.getSharedPreferences("trusted-v2",Context.MODE_PRIVATE).edit().clear().commit();
        if (!context.getSharedPreferences("pairing",Context.MODE_PRIVATE).edit().clear().commit()) throw new IllegalStateException("Trust reset failed");
        try { KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null); store.deleteEntry(ALIAS); } catch (Exception ignored) {}
    }
    @Override public synchronized void close() {
        closed = true; confirm(false);
        try { listener.close(); } catch (IOException ignored) {}
        LocalSocket socket = active; if (socket != null) try { socket.close(); } catch (IOException ignored) {}
        worker.interrupt(); pendingCode = null;
    }
}
