import { configure } from "@testing-library/react-native";

// Screens load through several (fake) API calls and the router; give findBy*/waitFor up to 5 s on a busy machine
// instead of the default 1 s. Assertions are unchanged.
configure({ asyncUtilTimeout: 5000 });
