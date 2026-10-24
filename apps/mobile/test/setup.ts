/* Test environment: a configured API address, in-memory secure storage and stubbed native pickers/files. */
process.env.EXPO_PUBLIC_API_BASE_URL = "http://api.test:5100";

jest.mock("expo-secure-store", () => {
  const store = new Map<string, string>();
  return {
    WHEN_UNLOCKED_THIS_DEVICE_ONLY: 6,
    __store: store,
    getItemAsync: jest.fn(async (key: string) => store.get(key) ?? null),
    setItemAsync: jest.fn(async (key: string, value: string) => void store.set(key, value)),
    deleteItemAsync: jest.fn(async (key: string) => void store.delete(key)),
  };
});

jest.mock("expo-document-picker", () => ({ getDocumentAsync: jest.fn() }));

jest.mock("expo-sharing", () => ({ isAvailableAsync: jest.fn(async () => true), shareAsync: jest.fn(async () => undefined) }));

jest.mock("expo-file-system", () => {
  class File {
    uri: string;
    constructor(...parts: unknown[]) {
      this.uri = parts.map((p) => (typeof p === "string" ? p : "cache")).join("/");
    }
    async bytes() {
      return new TextEncoder().encode(`bytes of ${this.uri}`);
    }
    static createDownloadTask = jest.fn(() => ({ downloadAsync: jest.fn(async () => ({ uri: "file:///cache/doc.pdf" })) }));
  }
  return { File, Paths: { cache: "cache" } };
});
