import { Tabs, usePathname } from "expo-router";
import { useEffect, useState } from "react";
import { Text } from "react-native";
import { api } from "../../../api/client";
import { useUser } from "../../../auth/AuthContext";
import { colors } from "../../../components/ui";
import { useI18n, type MessageKey } from "../../../i18n/I18nContext";

const ICONS: Record<string, string> = { index: "▦", requests: "☰", tasks: "✓", projects: "⌂", notifications: "🔔", more: "⋯" };

export default function TabsLayout() {
  const { t, rtl } = useI18n();
  const user = useUser();
  const pathname = usePathname();
  const [unread, setUnread] = useState(0);
  const isOffice = user.personas.some((p) => p === "consultant" || p === "contractor");

  // Refreshed whenever the user moves between screens (no push service needed for an in-app badge).
  useEffect(() => {
    let active = true;
    api.unreadNotifications().then((n) => active && setUnread(n)).catch(() => undefined);
    return () => {
      active = false;
    };
  }, [pathname]);

  const tabs: [string, MessageKey, boolean][] = [
    ["index", "tab.services", true],
    ["requests", "tab.requests", true],
    ["tasks", "tab.tasks", true],
    ["projects", "tab.projects", isOffice],
    ["notifications", "tab.notifications", true],
    ["more", "tab.more", true],
  ];
  const ordered = rtl ? [...tabs].reverse() : tabs;

  return (
    <Tabs screenOptions={{ tabBarActiveTintColor: colors.green, headerTitleAlign: "center" }}>
      {ordered.map(([name, label, visible]) => (
        <Tabs.Screen
          key={name}
          name={name}
          options={{
            title: t(label),
            href: visible ? undefined : null,
            tabBarAccessibilityLabel: name === "notifications" && unread > 0 ? `${t(label)} (${unread})` : t(label),
            tabBarBadge: name === "notifications" && unread > 0 ? unread : undefined,
            tabBarIcon: ({ color }) => <Text style={{ color, fontSize: 18 }}>{ICONS[name]}</Text>,
          }}
        />
      ))}
    </Tabs>
  );
}
