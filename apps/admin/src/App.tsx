import { Navigate, Route, Routes } from "react-router-dom";
import { RequireStaff } from "./auth/RequireStaff";
import { Layout } from "./components/Layout";
import { DashboardPage } from "./pages/DashboardPage";
import { LoginPage } from "./pages/LoginPage";
import { QueuePage } from "./pages/QueuePage";
import { ReviewRequestPage } from "./pages/ReviewRequestPage";
import { ReferenceDataProvider } from "./reference/ReferenceData";

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        element={
          <RequireStaff>
            <ReferenceDataProvider>
              <Layout />
            </ReferenceDataProvider>
          </RequireStaff>
        }
      >
        <Route path="/" element={<DashboardPage />} />
        <Route path="/requests" element={<QueuePage />} />
        <Route path="/requests/:id" element={<ReviewRequestPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
