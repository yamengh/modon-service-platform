import { Navigate, Route, Routes } from "react-router-dom";
import { RequireAuth } from "./auth/RequireAuth";
import { Layout } from "./components/Layout";
import { CoordinationLettersPage } from "./pages/CoordinationLettersPage";
import { ExcavationFormPage } from "./pages/ExcavationFormPage";
import { LoginPage } from "./pages/LoginPage";
import { MyRequestsPage } from "./pages/MyRequestsPage";
import { BuildingFormPage } from "./pages/BuildingFormPage";
import { ProjectPage } from "./pages/ProjectPage";
import { ProjectsPage } from "./pages/ProjectsPage";
import { RequestDetailPage } from "./pages/RequestDetailPage";
import { ServiceDetailPage } from "./pages/ServiceDetailPage";
import { ServicesPage } from "./pages/ServicesPage";
import { TasksPage } from "./pages/TasksPage";

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
        <Route path="/requests/:id/coordination-letters" element={<CoordinationLettersPage />} />
        <Route path="/tasks" element={<TasksPage />} />
        <Route path="/requests/:id/building" element={<BuildingFormPage />} />
        <Route path="/projects" element={<ProjectsPage />} />
        <Route path="/projects/:id" element={<ProjectPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/services" replace />} />
    </Routes>
  );
}
