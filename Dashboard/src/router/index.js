import { createRouter, createWebHistory } from "vue-router";
import HomeView from "../views/HomeView.vue";
import LoginView from "../views/LoginView.vue";
import ResetPasswordView from "../views/ResetPasswordView.vue";
import DashboardLayout from "../layouts/DashboardLayout.vue";
import DashboardHomeView from "../views/DashboardHomeView.vue";
import ChangePasswordView from "../views/ChangePasswordView.vue";
import UserManagementView from "../views/UserManagementView.vue";
import GenshinView from "../views/GenshinView.vue";
import HsrView from "../views/HsrView.vue";
import ZzzView from "../views/ZzzView.vue";
import Hi3View from "../views/Hi3View.vue";

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
          path: "genshin",
          name: "genshin",
          component: GenshinView,
        },
        {
          path: "hsr",
          name: "hsr",
          component: HsrView,
        },
        {
          path: "zzz",
          name: "zzz",
          component: ZzzView,
        },
        {
          path: "hi3",
          name: "hi3",
          component: Hi3View,
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
