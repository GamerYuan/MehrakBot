import { createRouter, createWebHistory } from "vue-router";
import HomeView from "../views/HomeView.vue";
import LoginView from "../views/LoginView.vue";
import ResetPasswordView from "../views/ResetPasswordView.vue";
import DocsView from "../views/DocsView.vue";
import DashboardLayout from "../layouts/DashboardLayout.vue";
import DashboardHomeView from "../views/DashboardHomeView.vue";
import ChangePasswordView from "../views/ChangePasswordView.vue";
import UserManagementView from "../views/UserManagementView.vue";
import DocsManagementView from "../views/DocsManagementView.vue";
import GameView from "../views/GameView.vue";
import SeaweedFilerView from "../views/SeaweedFilerView.vue";
import PrivacyPolicyView from "../views/PrivacyPolicyView.vue";
import TermsOfServiceView from "../views/TermsOfServiceView.vue";

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: "/",
      name: "home",
      component: HomeView,
    },
    {
      path: "/login",
      name: "login",
      component: LoginView,
    },
    {
      path: "/reset-password",
      name: "reset-password",
      component: ResetPasswordView,
    },
    {
      path: "/docs",
      name: "docs",
      component: DocsView,
    },
    {
      path: "/privacy",
      name: "privacy",
      component: PrivacyPolicyView,
    },
    {
      path: "/terms",
      name: "terms",
      component: TermsOfServiceView,
    },
    {
      path: "/dashboard",
      component: DashboardLayout,
      children: [
        {
          path: "",
          name: "dashboard-home",
          component: DashboardHomeView,
        },
        {
          path: "users",
          name: "user-management",
          component: UserManagementView,
        },
        {
          path: "docs",
          name: "docs-management",
          component: DocsManagementView,
        },
        {
          path: ":game",
          name: "game",
          component: GameView,
          beforeEnter: (to) => {
            const validGames = ["genshin", "hsr", "zzz", "hi3"];
            if (!validGames.includes(to.params.game)) {
              return { name: "dashboard-home" };
            }
          },
        },
        {
          path: "seaweed-filer",
          name: "seaweed-filer",
          component: SeaweedFilerView,
        },
        {
          path: "change-password",
          name: "change-password",
          component: ChangePasswordView,
        },
      ],
    },
  ],
});

export default router;