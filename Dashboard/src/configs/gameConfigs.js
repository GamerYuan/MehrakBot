export const gameConfigs = {
  genshin: {
    id: "Genshin",
    permission: "genshin",
    endpoint: "/genshin",
    title: "Genshin Impact",
    servers: [
      { value: "America", label: "America" },
      { value: "Europe", label: "Europe" },
      { value: "Asia", label: "Asia" },
      { value: "Sar", label: "TW/HK/MO" },
    ],
    tabs: [
      { id: "character", name: "Character", hasCharacterInput: true },
      {
        id: "abyss",
        name: "Spiral Abyss",
        hasFloorInput: true,
        floorMin: 9,
        floorMax: 12,
      },
      { id: "theater", name: "Imaginarium Theater" },
      { id: "stygian", name: "Stygian Onslaught" },
      { id: "charlist", name: "Character List" },
    ],
    hasCodesManagement: true,
    hasStatEdit: true,
    characterPlaceholder: "e.g. Nahida",
  },

  hsr: {
    id: "HonkaiStarRail",
    permission: "hsr",
    endpoint: "/hsr",
    title: "Honkai: Star Rail",
    servers: [
      { value: "America", label: "America" },
      { value: "Europe", label: "Europe" },
      { value: "Asia", label: "Asia" },
      { value: "Sar", label: "TW/HK/MO" },
    ],
    tabs: [
      { id: "character", name: "Character", hasCharacterInput: true },
      { id: "moc", name: "Memory of Chaos" },
      { id: "pf", name: "Pure Fiction" },
      { id: "as", name: "Apocalyptic Shadow" },
      { id: "aa", name: "Anomaly Arbitration" },
      { id: "charlist", name: "Character List" },
    ],
    hasCodesManagement: true,
    characterPlaceholder: "e.g. Firefly",
  },

  zzz: {
    id: "ZenlessZoneZero",
    permission: "zzz",
    endpoint: "/zzz",
    title: "Zenless Zone Zero",
    servers: [
      { value: "America", label: "America" },
      { value: "Europe", label: "Europe" },
      { value: "Asia", label: "Asia" },
      { value: "Sar", label: "TW/HK/MO" },
    ],
    tabs: [
      { id: "character", name: "Character", hasCharacterInput: true },
      { id: "shiyu", name: "Shiyu Defense" },
      { id: "da", name: "Deadly Assault" },
    ],
    hasCodesManagement: true,
    characterPlaceholder: "e.g. Ellen",
  },

  hi3: {
    id: "HonkaiImpact3",
    permission: "hi3",
    endpoint: "/hi3",
    title: "Honkai Impact 3rd",
    servers: [
      { value: "SEA", label: "SEA" },
      { value: "JP", label: "JP" },
      { value: "KR", label: "KR" },
      { value: "America", label: "America" },
      { value: "SAR", label: "TW/HK/MO" },
      { value: "Europe", label: "Europe" },
    ],
    tabs: [
      {
        id: "battlesuit",
        name: "Battlesuit",
        hasCharacterInput: true,
        characterLabel: "Battlesuit Name",
      },
    ],
    hasCodesManagement: false,
    characterPlaceholder: "e.g. White Comet",
  },
};
