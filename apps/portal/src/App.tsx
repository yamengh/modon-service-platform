import { Navigate, Route, Routes } from "react-router-dom";
import { RequireAuth } from "./auth/RequireAuth";
import { Layout } from "./components/Layout";
import { LoginPage } from "./pages/LoginPage";
import { ServiceDetailPage } from "./pages/ServiceDetailPage";
import { ServicesPage } from "./pages/ServicesPage";

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        element={
          <RequireAuth>
            <Layout />
          </RequireAuth>
        }
      >
        <Route path="/services" element={<ServicesPage />} />
        <Route path="/services/:code" element={<ServiceDetailPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/services" replace />} />
    </Routes>
  );
}
