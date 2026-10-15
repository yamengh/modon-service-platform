import { Navigate, Route, Routes } from "react-router-dom";
import { RequireAuth } from "./auth/RequireAuth";
import { Layout } from "./components/Layout";
import { ExcavationFormPage } from "./pages/ExcavationFormPage";
import { LoginPage } from "./pages/LoginPage";
import { MyRequestsPage } from "./pages/MyRequestsPage";
import { RequestDetailPage } from "./pages/RequestDetailPage";
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
        <Route path="/requests" element={<MyRequestsPage />} />
        <Route path="/requests/:id" element={<RequestDetailPage />} />
        <Route path="/requests/:id/edit" element={<ExcavationFormPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/services" replace />} />
    </Routes>
  );
}
