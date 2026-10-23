# USB pairing v2 — development protocol

Scope: authorized USB ADB transport, Android 12+ for Keystore ECDH (the tested
phone is Android 16), Windows current-user identity. This is a new protocol,
not an independently audited cryptographic product. No claims of forward
secrecy: persistent P-256 ECDH identities are used. A future audited authenticated
key exchange should be considered before broad production deployment.

1. Client sends TPM2 and SHA256(client P-256 SPKI || client random nonce32).
2. Server sends SHA256(server P-256 SPKI || server random nonce32).
3. Client reveals length-prefixed SPKI (91 bytes) and nonce32.
4. Server verifies client commitment, then reveals its SPKI and nonce32.
5. Client verifies server commitment. Both validate/import P-256 and agree ECDH.
6. Transcript = SHA256(ASCII TetherMax-Pair-v2 || clientPub || clientNonce ||
   serverPub || serverNonce). The role ordering and fixed lengths bind identities
   and both random values. Extract PRK = HMAC-SHA256(transcript, raw ECDH secret).
   Expand key = HMAC-SHA256(PRK, ASCII session || 0x01), a single HKDF block.
7. SAS = first unsigned big-endian uint32 of HMAC-SHA256(key, ASCII sas),
   reduced modulo 1,000,000 and padded to six decimal digits. Both commitments
   precede either reveal, preventing an adaptive choice after reading a peer
   nonce. The number is transient and never used as the transport password.
8. On first trust, both users approve matching SAS on their own screen. Client
   sends AES-256-GCM encryption of byte 1 with random nonce12 and transcript AAD.
   Server also requires local confirmation bound to the displayed session code.
   Expired dialogs cannot authorize a different pending code.
9. Server releases the existing random 128-bit transport bearer secret (32 ASCII
   hex characters) under AES-GCM with a fresh nonce12 and transcript AAD. Only
   successful authenticated decryption allows Windows to pin the phone identity.
   Three unknown-identity attempts per minute are allowed by each running server;
   one pairing socket is serviced at a time, with bounded reads/timeouts.

Android private identity: nonexportable AndroidKeyStore P-256 key, purpose
AGREE_KEY. Trusted Windows public-key fingerprints are app-private preferences;
they are public information. The bearer secret remains AES-GCM wrapped by the
existing Keystore AES key. Windows PKCS8 private identity and pinned phone SPKI
are DPAPI CurrentUser-protected. A replacement/missing/corrupt identity fails
closed rather than silently replacing existing trust. Known peers still perform
fresh nonce-bound ECDH key confirmation, without repeating a consent dialog.

Windows sends the bearer to its child Go process through a private inherited
stdin pipe, bounded to one line. No secret argument, UI reveal or global
environment variable is required. Existing v1 data streams remain bearer
authenticated over ADB; this change does not encrypt every v1 relay frame.

Revocation: Windows forget removes its pinned phone. Full re-pairing requires
forget on Android too. Android forget stops its service, clears trust, deletes
its ECDH identity and clears the wrapped bearer record so next start creates a
fresh bearer. Known active processes must close before upgrade/uninstall.

Windows ADB sometimes reports get-devpath=unknown. Discovery then requires a
non-network ADB serial equal to device ro.serialno and sys.usb.state containing
adb. This is transport hygiene, not cryptographic attestation. Physical SAS
confirmation and pinned keys provide peer authentication. A hostile already
authorized ADB host is outside this preview's isolation guarantee.

References: [Android Keystore ECDH](https://developer.android.com/reference/android/security/keystore/KeyProperties#PURPOSE_AGREE_KEY),
[DPAPI current-user scope](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope).
