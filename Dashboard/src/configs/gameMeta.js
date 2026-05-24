import { gameConfigs } from "./gameConfigs";

export const gameMeta = {
  Genshin: {
    label: "Genshin Impact",
    shortLabel: "Genshin",
    color: "#FFD700",
    bgColor: "rgba(255, 215, 0, 0.15)",
    borderColor: "rgba(255, 215, 0, 0.4)",
    permission: "genshin",
    routeKey: "genshin",
  },
  HonkaiStarRail: {
    label: "Honkai: Star Rail",
    shortLabel: "HSR",
    color: "#00D4FF",
    bgColor: "rgba(0, 212, 255, 0.15)",
    borderColor: "rgba(0, 212, 255, 0.4)",
    permission: "hsr",
    routeKey: "hsr",
  },
  ZenlessZoneZero: {
    label: "Zenless Zone Zero",
    shortLabel: "ZZZ",
    color: "#FF6B00",
    bgColor: "rgba(255, 107, 0, 0.15)",
    borderColor: "rgba(255, 107, 0, 0.4)",
    permission: "zzz",
    routeKey: "zzz",
  },
  HonkaiImpact3: {
    label: "Honkai Impact 3rd",
    shortLabel: "HI3",
    color: "#FF69B4",
    bgColor: "rgba(255, 105, 180, 0.15)",
    borderColor: "rgba(255, 105, 180, 0.4)",
    permission: "hi3",
    routeKey: "hi3",
  },
  TearsOfThemis: {
    label: "Tears of Themis",
    shortLabel: "ToT",
    color: "#C8A2C8",
    bgColor: "rgba(200, 162, 200, 0.15)",
    borderColor: "rgba(200, 162, 200, 0.4)",
    permission: "tot",
    routeKey: "tot",
  },
  Unsupported: {
    label: "Miscellaneous",
    shortLabel: "Misc",
    color: "#888888",
    bgColor: "rgba(136, 136, 136, 0.15)",
    borderColor: "rgba(136, 136, 136, 0.4)",
    permission: null,
    routeKey: null,
  },
};

export const gameOptions = Object.entries(gameMeta)
  .filter(([key]) => key !== "Unsupported")
  .map(([value, meta]) => ({ label: meta.label, value }))
  .concat([{ label: "Miscellaneous", value: "Unsupported" }]);

export const gameFilterOptions = [
  { label: "All Games", value: "All" },
  ...gameOptions,
];

export const permissionLabels = Object.fromEntries(
  Object.values(gameMeta)
    .filter((m) => m.permission)
    .map((m) => [m.permission, m.label]),
);

export const availablePermissions = Object.keys(permissionLabels);

export const gameLabels = Object.fromEntries(
  Object.entries(gameMeta).map(([key, meta]) => [key, meta.label]),
);