import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { App } from "../App";
import type { PersonaResponse, ServiceDetail } from "../api/types";
import { AuthProvider } from "../auth/AuthContext";
import { I18nProvider } from "../i18n/I18nContext";
import type { Lang } from "../i18n/messages";

/** Renders the whole app at a route. Tests mock ../api/client to control backend responses. */
export function renderApp(route: string, lang: Lang = "ar") {
  return render(
    <I18nProvider lang={lang}>
      <AuthProvider>
        <MemoryRouter initialEntries={[route]}>
          <App />
        </MemoryRouter>
      </AuthProvider>
    </I18nProvider>,
  );
}

export const investor: PersonaResponse = {
  id: "u1",
  email: "investor@modon.local",
  name: "investor@modon.local",
  roles: ["investor"],
  personas: ["investor"],
  canUsePortal: true,
};

export const reviewer: PersonaResponse = {
  id: "u2",
  email: "reviewer@modon.local",
  name: "reviewer@modon.local",
  roles: ["modon_reviewer"],
  personas: [],
  canUsePortal: false,
};

export const buildingLicence: ServiceDetail = {
  id: 1,
  code: "building-licence",
  nameAr: "رخصة البناء وتقارير البناء",
  nameEn: "Building Licence & Construction Reports",
  summaryAr: "ملخص رخصة البناء",
  summaryEn: "Building licence summary",
  beneficiary: "investor",
  sortOrder: 1,
};

export const excavationPermit: ServiceDetail = {
  id: 2,
  code: "excavation-permit",
  nameAr: "تصريح أعمال الحفر",
  nameEn: "Excavation Works Permit",
  summaryAr: "ملخص تصريح الحفر",
  summaryEn: "Excavation permit summary",
  beneficiary: "contractor",
  durationAr: "3 أيام عمل",
  durationEn: "3 working days",
  sortOrder: 2,
  stepsAr: "الدخول على بوابة مدن\nاختيار خدمات مدن\nاختيار تصريح أعمال الحفر",
  stepsEn: "Sign in\nOpen Modon services\nChoose Excavation Works Permit",
  requiredDocumentsAr: "مخطط مسار العمل\nخطة سلامة السير",
  requiredDocumentsEn: "Work route plan\nTraffic safety plan",
};
