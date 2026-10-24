import { useFocusEffect } from "expo-router";
import { useCallback, useRef, useState } from "react";

export type Load<T> = { status: "loading" } | { status: "error"; error: unknown } | { status: "ready"; data: T };

/**
 * Loads data when the screen gains focus (so lists are fresh after acting elsewhere), with pull-to-refresh.
 * A refresh that fails keeps the data already shown.
 */
export function useLoad<T>(fetcher: () => Promise<T>, deps: unknown[]) {
  const [load, setLoad] = useState<Load<T>>({ status: "loading" });
  const [refreshing, setRefreshing] = useState(false);
  const hasData = useRef(false);
  const fetch = useCallback(fetcher, deps);

  const run = useCallback(
    async (refresh: boolean) => {
      if (refresh) setRefreshing(true);
      try {
        const data = await fetch();
        hasData.current = true;
        setLoad({ status: "ready", data });
      } catch (error) {
        if (!hasData.current) setLoad({ status: "error", error });
      } finally {
        setRefreshing(false);
      }
    },
    [fetch],
  );

  useFocusEffect(
    useCallback(() => {
      void run(false);
    }, [run]),
  );

  const setData = useCallback((data: T) => {
    hasData.current = true;
    setLoad({ status: "ready", data });
  }, []);

  return { load, refreshing, refresh: () => void run(true), setData };
}
