import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { Platform } from "react-native";
import { api, ApiError, setSessionToken, setUnauthorizedHandler } from "../api/client";
import type { PersonaResponse } from "../api/types";
import { tokenStorage } from "./tokenStorage";

/**
 * - restoring: reading the stored token and asking the backend who it belongs to;
 * - signedOut: no session (reason says why, e.g. the session expired);
 * - offline: a token is stored but the backend cannot be reached — the token is kept, the user can retry;
 * - signedIn: the backend confirmed the token.
 */
export type AuthState =
  | { status: "restoring" }
  | { status: "signedOut"; reason?: "expired" | "loggedOut" }
  | { status: "offline" }
  | { status: "signedIn"; user: PersonaResponse };

interface Auth {
  state: AuthState;
  /** Throws ApiError: 401 invalidCredentials / lockedOut, 403 roleNotAllowed, 0 network / apiNotConfigured. */
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  retry: () => Promise<void>;
}

const AuthContext = createContext<Auth | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: "restoring" });

  const endSession = useCallback(async (reason: "expired" | "loggedOut") => {
    setSessionToken(null);
    try {
      await tokenStorage.clear();
    } catch {
      // nothing stored
    }
    setState({ status: "signedOut", reason });
  }, []);

  const restore = useCallback(async () => {
    setState({ status: "restoring" });
    let stored: string | null = null;
    try {
      stored = await tokenStorage.read();
    } catch {
      stored = null;
    }
    if (!stored) return setState({ status: "signedOut" });
    setSessionToken(stored);
    try {
      setState({ status: "signedIn", user: await api.me() });
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) await endSession("expired");
      else setState({ status: "offline" });
    }
  }, [endSession]);

  useEffect(() => {
    // Any 401 on an authenticated call means the server no longer accepts the token.
    setUnauthorizedHandler(() => void endSession("expired"));
    void restore();
    return () => setUnauthorizedHandler(null);
  }, [restore, endSession]);

  const login = useCallback(async (email: string, password: string) => {
    const result = await api.login(email.trim(), password, `${Platform.OS} app`);
    await tokenStorage.write(result.token);
    setSessionToken(result.token);
    setState({ status: "signedIn", user: result.user });
  }, []);

  const logout = useCallback(async () => {
    try {
      await api.logout(); // revoke on the server; works offline too (the token is forgotten locally anyway)
    } catch {
      // offline or already expired
    }
    await endSession("loggedOut");
  }, [endSession]);

  const value = useMemo<Auth>(() => ({ state, login, logout, retry: restore }), [state, login, logout, restore]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): Auth {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}

export function useUser(): PersonaResponse {
  const { state } = useAuth();
  if (state.status !== "signedIn") throw new Error("useUser needs a signed-in user");
  return state.user;
}
