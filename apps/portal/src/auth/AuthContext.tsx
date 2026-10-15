import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api } from "../api/client";
import type { PersonaResponse } from "../api/types";

type AuthState =
  | { status: "loading" }
  | { status: "anonymous" }
  | { status: "authenticated"; user: PersonaResponse };

interface Auth {
  state: AuthState;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<Auth | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: "loading" });

  const refresh = useCallback(async () => {
    try {
      const user = await api.getPersonas();
      setState(user ? { status: "authenticated", user } : { status: "anonymous" });
    } catch {
      setState({ status: "anonymous" });
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const value = useMemo<Auth>(
    () => ({
      state,
      login: async (email, password) => {
        await api.login(email, password);
        await refresh();
      },
      logout: async () => {
        try {
          await api.logout();
        } finally {
          setState({ status: "anonymous" });
        }
      },
    }),
    [state, refresh],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): Auth {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}
