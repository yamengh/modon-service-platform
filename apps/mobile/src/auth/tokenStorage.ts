import * as SecureStore from "expo-secure-store";

/**
 * The mobile session token lives only in the platform's secure storage (Android Keystore / iOS Keychain),
 * never in AsyncStorage or plain files. WHEN_UNLOCKED_THIS_DEVICE_ONLY: readable only while the device is
 * unlocked, and not copied into backups or to another device.
 */
const KEY = "modon.session.token";
const OPTIONS: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY };

export const tokenStorage = {
  read: () => SecureStore.getItemAsync(KEY, OPTIONS),
  write: (token: string) => SecureStore.setItemAsync(KEY, token, OPTIONS),
  clear: () => SecureStore.deleteItemAsync(KEY, OPTIONS),
};
