<script setup>
import { ref } from "vue";
import Card from "primevue/card";
import Button from "primevue/button";

const releases = [
  {
    version: "v1.1.0",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          { type: "feature", text: "Updated website for public access" },
          {
            type: "improvement",
            text: "[profile] Improved handling of profile update and profile delete. You can now select a profile with both the Profile ID and HoYoLAB UID",
          },
          {
            type: "feature",
            text: "[hyl] Added command. This command allows you to embed a HoYoLAB post in Discord by providing the post URL. It supports all languages that HoYoLAB ly supports",
          },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "fix",
            text: "[character] Fixed an issue whereby some character splash arts might be replaced by relic icons due to overlapping ID space",
          },
          {
            type: "fix",
            text: '[charlist] Fixed an issue whereby the level display incorrectly states "AR" instead of "TB"',
          },
          {
            type: "improvement",
            text: "[pf] [as] Updated the layout for buff icons such that they are better centered",
          },
        ],
      },
      {
        name: "ZZZ Toolbox",
        notes: [
          {
            type: "feature",
            text: "[shiyu] Re-enabled command with support for Shiyu Defense V2.",
          },
        ],
      },
      {
        name: "Behind the scenes",
        notes: [
          {
            type: "improvement",
            text: "Added a source generator for generating help string snippets for commands",
          },
          {
            type: "feature",
            text: "Added support for SeaweedFS Filer reverse proxy for Dashboard super admin users",
          },
        ],
      },
    ],
  },
  {
    version: "v1.0.0",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          { type: "improvement", text: "[help] Now displays the bot version" },
          {
            type: "improvement",
            text: "[health] Now displays the bot version",
          },
          {
            type: "feature",
            text: "[profile] Added a new subcommand `update` to allow changing of Cookies and Passphrase without needing to delete and re-add the profile",
          },
          {
            type: "improvement",
            text: "Updated rate limiting to allow for small burst of requests within a short period of time",
          },
          {
            type: "feature",
            text: "Added Discord Rich Presence support to display registered user count",
          },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "feature",
            text: "[character] Added support for ascension level cap for characters",
          },
          {
            type: "feature",
            text: "[character] Added support for multiple card generation per command with comma-separated character names/aliases",
          },
          {
            type: "fix",
            text: "[notes] Fixed an issue whereby the command did not work",
          },
          {
            type: "fix",
            text: "[theater] Fixed an issue whereby the generated card might display multiple Arcana stages clear under certain circumstances",
          },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "feature",
            text: "[character] Added support for Elation characters",
          },
          {
            type: "feature",
            text: "[character] Added support for multiple card generation per command with comma-separated character names/aliases",
          },
          {
            type: "fix",
            text: "[notes] Fixed an issue whereby the command did not work",
          },
        ],
      },
      {
        name: "ZZZ Toolbox",
        notes: [
          { type: "feature", text: "[charlist] Added command" },
          { type: "feature", text: "[tower] Added command" },
          {
            type: "improvement",
            text: "[shiyu] Temporarily disabled command to prepare for Shiyu Defense V2 support",
          },
          {
            type: "fix",
            text: "[da] Fixed an issue whereby the buff icons are not scaled properly",
          },
          {
            type: "fix",
            text: "[notes] Fixed an issue whereby the command did not work",
          },
          {
            type: "fix",
            text: "Fixed an issue whereby character avatar icons may use skins for characters regardless of ownership",
          },
        ],
      },
      {
        name: "HI3 Toolbox",
        notes: [
          {
            type: "fix",
            text: "[battlesuit] Fixed an issue whereby card generation will fail when one of the outfits owned does not exist on HoYoLAB API",
          },
          {
            type: "fix",
            text: "[battlesuit] Fixed an issue whereby the weapon name might overshoot the container width",
          },
        ],
      },
      {
        name: "Behind the scenes",
        notes: [
          {
            type: "improvement",
            text: "Migrated metrics server to ClickHouse",
          },
          {
            type: "improvement",
            text: "Extracted core business logic as a standalone gRPC service",
          },
          { type: "feature", text: "Added Dashboard website for maintainers" },
        ],
      },
    ],
  },
  {
    version: "v0.9.2",
    date: "",
    sections: [
      {
        name: "ZZZ Toolbox",
        notes: [
          { type: "feature", text: "[character] Added support for Honed Edge" },
        ],
      },
    ],
  },
  {
    version: "v0.9.1",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "fix",
            text: "[help] Fixed an issue whereby `zzz da` and `zzz shiyu` is not listed as available commands in the base help command",
          },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "fix",
            text: "[character] Fixed an issue whereby Normal Attack talents enhanced by unlocked constellations are not properly highlighted",
          },
        ],
      },
    ],
  },
  {
    version: "v0.9.0",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "fix",
            text: "Fixed an issue whereby using a command without a profile does not return a valid response",
          },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "feature",
            text: "[character] Add support for ascended image icons",
          },
          {
            type: "feature",
            text: "[character] Highlight talent levels enhanced by unlocked constellations",
          },
          {
            type: "feature",
            text: "[character] Add support to fetch HoYoWiki image from multiple locales",
          },
          {
            type: "feature",
            text: "[character] Add support to auto update autocomplete list as users use the `character` or `charlist` commands",
          },
          {
            type: "feature",
            text: "[charlist] Add support for ascended image icons",
          },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "feature",
            text: "[aa] Add Anomaly Arbitration summary card",
          },
          {
            type: "feature",
            text: "[character] Add support to fetch HoYoWiki image from multiple locales",
          },
          {
            type: "feature",
            text: "[character] Add support to auto update autocomplete list as users use the `character` or `charlist` commands",
          },
          { type: "feature", text: "[notes] Add support for Currency Wars" },
        ],
      },
      {
        name: "ZZZ Toolbox",
        notes: [
          {
            type: "feature",
            text: "[character] Add support to fetch HoYoWiki image from multiple locales",
          },
          {
            type: "feature",
            text: "[character] Add support to auto update autocomplete list as users use the `character` command",
          },
          {
            type: "fix",
            text: "[da] Fixed an issue whereby summary card will fail to generate if duplicate buffs are selected",
          },
        ],
      },
      {
        name: "HI3 Toolbox",
        notes: [
          {
            type: "feature",
            text: "[battlesuit] Add HI3 battlesuit summary card",
          },
        ],
      },
      {
        name: "Behind the scenes",
        notes: [
          { type: "improvement", text: "Upgrade to .NET 10" },
          { type: "improvement", text: "Bump dependencies" },
          {
            type: "improvement",
            text: "Migrated Database from MongoDB to SQL",
          },
        ],
      },
    ],
  },
  {
    version: "v0.8.0",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "improvement",
            text: "[checkin] Check in command now only checks in for games you have an account for",
          },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "fix",
            text: "[charlist] Fixed an issue whereby Sangonomiya Kokomi's name will exceed the bounds",
          },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "fix",
            text: "[character] Fixed an issue whereby Preservation characters' talent icons are incorrectly positioned",
          },
        ],
      },
      {
        name: "Behind the scenes",
        notes: [
          { type: "improvement", text: "Restructured codebase" },
          { type: "improvement", text: "Better API logging" },
          { type: "improvement", text: "Better metrics collection" },
          { type: "improvement", text: "Bump dependencies" },
        ],
      },
    ],
  },
  {
    version: "v0.7.2",
    date: "",
    sections: [
      {
        name: "ZZZ Toolbox",
        notes: [
          {
            type: "fix",
            text: "[character] Fixed an issue whereby character cards will sometimes fail fetching splash art for card generation",
          },
        ],
      },
    ],
  },
  {
    version: "v0.7.1",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "fix",
            text: "[help] Fixed an issue whereby new additions are not reflected in the help commands",
          },
        ],
      },
    ],
  },
  {
    version: "v0.7.0",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "feature",
            text: "[checkin] Added support for Tears of Themis daily check-in",
          },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "feature",
            text: "[genshin] Added support for Imaginarium Theater Lunar Mode",
          },
        ],
      },
      {
        name: "ZZZ Toolbox",
        notes: [
          { type: "feature", text: "[shiyu] Added command" },
          { type: "feature", text: "[da] Added command" },
          { type: "feature", text: "[notes] Added command" },
        ],
      },
    ],
  },
  {
    version: "v0.6.1",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "fix",
            text: "Fixed a potential security issue with how token caches are stored and accessed. No instance of exploitation of this issue was discovered",
          },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "fix",
            text: "[character] Fixed an issue whereby card relic set names are not shown under certain circumstances",
          },
        ],
      },
    ],
  },
  {
    version: "v0.6.0",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "improvement",
            text: "[help] Updated to reflect new additions",
          },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "fix",
            text: "[character] Fixed an issue whereby the character `Candace` would display incorrect skill icons",
          },
          {
            type: "fix",
            text: "[charlist] Fixed an issue whereby the response does not have a `Remove` button",
          },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          { type: "feature", text: "[charlist] Added command" },

          {
            type: "fix",
            text: "[as][pf] Fixed an issue whereby displayed buff icons have incorrect size under certain circumstances",
          },
        ],
      },
      {
        name: "ZZZ Toolbox",
        notes: [
          {
            type: "feature",
            text: "[character] Added command",
          },
        ],
      },
      {
        name: "Behind the Scenes",
        notes: [
          {
            type: "improvement",
            text: "Reduced the cold start time for a command's first use since the bot's boot up",
          },
        ],
      },
    ],
  },
  {
    version: "v0.5.0",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "improvement",
            text: "[help] Updated to reflect new additions",
          },
          {
            type: "fix",
            text: "Fixed an issue whereby authentication failure will send 2 error messages instead of 1",
          },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "improvement",
            text: "[codes] Optimised the redemption time required",
          },
          { type: "feature", text: "[charlist] Added command" },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "fix",
            text: "[moc] Fixed an issue whereby the command throws an error for incomplete clears",
          },
          {
            type: "improvement",
            text: "[codes] Optimised the redemption time",
          },
          { type: "feature", text: "[pf] Added command" },
          { type: "feature", text: "[as] Added command" },
        ],
      },
      {
        name: "ZZZ Toolbox",
        notes: [
          {
            type: "improvement",
            text: "[codes] Optimised the redemption time",
          },
        ],
      },
    ],
  },
  {
    version: "v0.4.1",
    date: "",
    sections: [
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "fix",
            text: "[codes] Fixed an issue whereby multiple code redeems does not work due to short redemption interval",
          },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "fix",
            text: "[moc] Fixed an issue whereby Memory of Chaos card container shows incorrect timestamp",
          },
          {
            type: "fix",
            text: "[codes] Fixed an issue whereby multiple code redeems does not work due to short redemption interval",
          },
        ],
      },
      {
        name: "ZZZ Toolbox",
        notes: [
          {
            type: "fix",
            text: "[codes] Fixed an issue whereby multiple code redeems does not work due to short redemption interval",
          },
        ],
      },
    ],
  },
  {
    version: "v0.4.0",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "improvement",
            text: "[help] Updated to reflect new additions",
          },
          { type: "improvement", text: "[health] Updated layout" },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "improvement",
            text: "[abyss] Spiral Abyss summary card now fills up empty space for uncleared chambers",
          },
          {
            type: "fix",
            text: "[abyss] Fixed an issue whereby Spiral Abyss summary card shows incorrect cycle dates",
          },
          {
            type: "improvement",
            text: "[codes] Command has been updated to redeem all known codes at once. Codes can still be manually specified and valid codes will be automatically added as known codes",
          },
          { type: "feature", text: "[stygian] Added command" },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          { type: "feature", text: "[moc] Added command" },
          {
            type: "improvement",
            text: "[codes] Command has been updated to redeem all known codes at once. Codes can still be manually specified and valid codes will be automatically added as known codes",
          },
        ],
      },
      {
        name: "ZZZ Toolbox",
        notes: [{ type: "feature", text: "[codes] Added command" }],
      },
    ],
  },
  {
    version: "v0.3.0",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "improvement",
            text: "[help] Updated to reflect new additions",
          },
          {
            type: "improvement",
            text: "Reduced authentication time-out period to 1 minute",
          },
          { type: "improvement", text: "Streamlined error messages" },
          { type: "improvement", text: "Better internal error logging" },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "feature",
            text: "[character] Added autocomplete for character names",
          },
          {
            type: "feature",
            text: "[character] Added aliasing for character names",
          },
          {
            type: "improvement",
            text: "[character] Updated Cryo and Anemo background colour for character cards",
          },
          {
            type: "improvement",
            text: "[character] Updated character card portrait image placement, such that most character portraits will be a better fit",
          },
          { type: "feature", text: "[theater] Added command" },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "feature",
            text: "[character] Added aliasing for character names",
          },
        ],
      },
    ],
  },
  {
    version: "v0.2.5",
    date: "",
    sections: [
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "fix",
            text: "[character] Fixed an issue whereby the command fails prematurely due to missing Light Cone wiki entry from HoYoLAB API",
          },
        ],
      },
    ],
  },
  {
    version: "v0.2.4",
    date: "",
    sections: [
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "fix",
            text: "[abyss] Fixed an issue whereby the command will fail if the floor requested does not contain a character used on a different floor",
          },
          {
            type: "fix",
            text: "[abyss] Fixed an issue whereby abyss card generated does not have properly dimmed stars",
          },
          {
            type: "fix",
            text: "[notes] Fixed an issue whereby they do not have the proper display text for when all expeditions/assignments are completed",
          },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          {
            type: "fix",
            text: "[notes] Fixed an issue whereby they do not have the proper display text for when all expeditions/assignments are completed",
          },
        ],
      },
    ],
  },
  {
    version: "v0.2.2",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          { type: "improvement", text: "Added metrics for new commands" },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          {
            type: "fix",
            text: "[abyss] Fixed an issue whereby the command will fail if any character in Most Used Character list does not appear in the battles",
          },
        ],
      },
    ],
  },
  {
    version: "v0.2.1",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          {
            type: "feature",
            text: "[health] Added command (Only available for server admins)",
          },
          {
            type: "improvement",
            text: "[help] Updated to reflect the new additions",
          },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          { type: "feature", text: "[abyss] Added command" },
          { type: "feature", text: "[codes] Added command" },
          { type: "feature", text: "[notes] Added command" },
          {
            type: "improvement",
            text: "[character] Dimmed flat stats for Artifacts",
          },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [
          { type: "feature", text: "[codes] Added command" },
          { type: "feature", text: "[notes] Added command" },
          {
            type: "improvement",
            text: "[character] Dimmed flat stats for Relics",
          },
        ],
      },
    ],
  },
  {
    version: "v0.1.1",
    date: "",
    sections: [
      {
        name: "Common",
        notes: [
          { type: "feature", text: "[profile] Multi-profile support" },
          { type: "feature", text: "[checkin] Added command" },
        ],
      },
      {
        name: "Genshin Toolbox",
        notes: [
          { type: "feature", text: "[character] Genshin Character Card" },
        ],
      },
      {
        name: "HSR Toolbox",
        notes: [{ type: "feature", text: "[character] HSR Character Card" }],
      },
    ],
  },
];

const getTypeOrder = (type) => {
  const order = { feature: 0, improvement: 1, fix: 2 };
  return order[type] ?? 3;
};

const getFirstCommand = (text) => {
  const match = text.match(/\[([^\]]+)\]/);
  return match ? match[1] : "\uFFFF";
};

const sortedReleases = releases.map((release) => ({
  ...release,
  sections: release.sections.map((section) => ({
    ...section,
    notes: [...section.notes].sort((a, b) => {
      const typeOrderA = getTypeOrder(a.type);
      const typeOrderB = getTypeOrder(b.type);
      if (typeOrderA !== typeOrderB) {
        return typeOrderA - typeOrderB;
      }
      const cmdA = getFirstCommand(a.text);
      const cmdB = getFirstCommand(b.text);
      return cmdA.localeCompare(cmdB);
    }),
  })),
}));

const selectedVersion = ref(sortedReleases[0].version);

const scrollToVersion = (version) => {
  selectedVersion.value = version;
  const element = document.getElementById(`release-${version}`);
  if (element) {
    element.scrollIntoView({ behavior: "smooth", block: "start" });
  }
};

const getTypeLabel = (type) => {
  const labels = {
    feature: { label: "Feature", class: "bg-emerald-500/20 text-emerald-300" },
    improvement: {
      label: "Improvement",
      class: "bg-blue-500/20 text-blue-300",
    },
    fix: { label: "Fix", class: "bg-orange-500/20 text-orange-300" },
  };
  return labels[type] || { label: type, class: "bg-zinc-500/20 text-zinc-300" };
};

const parseNoteText = (text) => {
  const parts = [];
  const regex = /\[([^\]]+)\]/g;
  let lastIndex = 0;
  let match;
  let hasCommands = false;

  while ((match = regex.exec(text)) !== null) {
    hasCommands = true;
    if (match.index > lastIndex) {
      parts.push({ type: "text", text: text.slice(lastIndex, match.index) });
    }
    parts.push({ type: "command", text: match[1] });
    lastIndex = match.index + match[0].length;
  }

  if (lastIndex < text.length) {
    parts.push({ type: "text", text: text.slice(lastIndex) });
  }

  if (!hasCommands) {
    return [{ type: "text", text: text }];
  }

  return parts;
};

const scrollToTop = () => {
  window.scrollTo({ top: 0, behavior: "smooth" });
};
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
          Release Notes
        </h2>
        <p class="text-zinc-300 leading-relaxed">
          Track important documentation updates and behavior changes between bot
          releases
        </p>
      </template>
    </Card>

    <div class="grid grid-cols-1 lg:grid-cols-[1fr_140px] gap-6">
      <div class="flex flex-col gap-4">
        <Card
          v-for="release in sortedReleases"
          :key="release.version"
          :id="`release-${release.version}`"
          class="bg-white/5 border border-white/10 rounded-2xl"
        >
          <template #content>
            <div class="flex flex-col gap-4">
              <div class="flex items-center justify-between">
                <h3 class="text-xl font-bold text-zinc-100 font-mono">
                  {{ release.version }}
                </h3>
                <span v-if="release.date" class="text-sm text-zinc-500">{{
                  release.date
                }}</span>
              </div>

              <div
                v-for="section in release.sections"
                :key="section.name"
                class="flex flex-col gap-3"
              >
                <h4
                  class="text-sm font-semibold text-zinc-400 uppercase tracking-wider border-b border-white/10 pb-2"
                >
                  {{ section.name }}
                </h4>
                <ul class="flex flex-col gap-2">
                  <li
                    v-for="(note, index) in section.notes"
                    :key="index"
                    class="flex items-start gap-3"
                  >
                    <div class="flex items-center gap-1.5 shrink-0 flex-wrap">
                      <span
                        :class="[
                          'text-xs font-semibold px-2 py-1 rounded',
                          getTypeLabel(note.type).class,
                        ]"
                      >
                        {{ getTypeLabel(note.type).label }}
                      </span>
                      <template
                        v-for="(part, pIndex) in parseNoteText(note.text)"
                        :key="pIndex"
                      >
                        <span
                          v-if="part.type === 'command'"
                          class="text-xs font-semibold px-1.5 py-1 rounded bg-violet-500/20 text-violet-300"
                        >
                          {{ part.text }}
                        </span>
                      </template>
                    </div>
                    <span class="text-zinc-300 leading-relaxed flex-1 min-w-0">
                      <template
                        v-for="(part, pIndex) in parseNoteText(note.text)"
                        :key="pIndex"
                      >
                        <span v-if="part.type === 'text'">{{ part.text }}</span>
                      </template>
                    </span>
                  </li>
                </ul>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <aside class="lg:sticky lg:top-28 h-fit">
        <Card class="bg-white/5 border border-white/10 rounded-2xl">
          <template #content>
            <h4
              class="text-sm font-semibold text-zinc-400 uppercase tracking-wider mb-3"
            >
              Versions
            </h4>
            <nav
              class="flex flex-col gap-1 max-h-80 overflow-y-auto pr-1 scrollbar-thin scrollbar-thumb-white/10 scrollbar-track-transparent"
            >
              <button
                v-for="release in sortedReleases"
                :key="release.version"
                type="button"
                :class="[
                  'text-left px-2 py-1.5 rounded-lg text-sm transition-all',
                  selectedVersion === release.version
                    ? 'text-white bg-emerald-500/20 border border-emerald-500/50'
                    : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/5',
                ]"
                @click="scrollToVersion(release.version)"
              >
                <span class="font-mono text-xs">{{ release.version }}</span>
              </button>
            </nav>
          </template>
        </Card>
      </aside>
    </div>

    <div class="fixed bottom-6 right-6 z-50">
      <Button
        icon="pi pi-arrow-up"
        rounded
        severity="secondary"
        class="bg-emerald-500/20! border-emerald-500/50! text-emerald-300! hover:bg-emerald-500/30! backdrop-blur-sm shadow-lg"
        @click="scrollToTop"
      >
      </Button>
    </div>
  </div>
</template>
