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
import { gameMeta } from "../configs/gameMeta";

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  scrollBehavior(to, from, savedPosition) {
    if (to.hash) {
      return { el: to.hash, behavior: "smooth" };
    }
    if (savedPosition) {
      return savedPosition;
    }
    return { top: 0 };
  },
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
        component: () => import("@/views/DocsManagementView.vue"),
      },
      {
        path: "release-notes",
        name: "release-notes-management",
        component: () => import("@/views/ReleaseNotesManagementView.vue"),
      },
        {
          path: ":game",
          name: "game",
          component: GameView,
          beforeEnter: (to) => {
            const validGames = Object.values(gameMeta)
              .map((m) => m.routeKey)
              .filter(Boolean);
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

router.beforeEach((to) => {
  if (to.path.startsWith("/dashboard")) {
    const storedUser = localStorage.getItem("mehrak_user");
    if (!storedUser) {
      return { name: "login" };
    }
  }
});

export default router;
