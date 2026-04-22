<script setup>
import { useRouter } from "vue-router";
import Stepper from "primevue/stepper";
import StepItem from "primevue/stepitem";
import Step from "primevue/step";
import StepPanel from "primevue/steppanel";
import Button from "primevue/button";
import Message from "primevue/message";
import Image from "primevue/image";
import Card from "primevue/card";

const router = useRouter();
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <div class="flex flex-col gap-4">
          <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
            Getting Started
          </h2>
          <p class="text-zinc-300 leading-relaxed">
            You can use Mehrak by inviting Mehrak to your Discord server, or
            installing Mehrak to your Discord user profile.
          </p>
        </div>

        <div class="mt-8 pt-6 border-t border-white/10">
          <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-6">
            Inviting to Discord Server
          </h3>
          
          <Message severity="info" :closable="false" class="mb-4">
            You are required to have
            <code
              class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
              >Manage Server</code
            >
            or
            <code
              class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
              >Administrator</code
            >
            permissions in a server to invite Discord bots into that server.
          </Message>

          <Stepper value="1">
            <StepItem value="1">
              <Step value="1">Install Page</Step>
              <StepPanel v-slot="{ activateCallback }" value="1">
                <div class="flex flex-col gap-4 py-4 text-zinc-300">
                  <p>
                    Click this link to open the
                    <a
                      href="https://discord.com/oauth2/authorize?client_id=1365154828430610532"
                      target="_blank"
                      rel="noopener noreferrer"
                      class="text-emerald-400 hover:text-emerald-300 transition-colors"
                      >Mehrak install page</a
                    >
                    and select
                    <strong class="text-zinc-200">Add to Server</strong>.
                  </p>
                  <div class="flex gap-2">
                    <Button
                      size="small"
                      label="Next"
                      @click="activateCallback('2')"
                    />
                  </div>
                </div>
              </StepPanel>
            </StepItem>

            <StepItem value="2">
              <Step value="2">Select Server</Step>
              <StepPanel v-slot="{ activateCallback }" value="2">
                <div class="flex flex-col gap-4 py-4 text-zinc-300">
                  <p>Select the server where you want Mehrak to be installed.</p>
                  <div class="flex gap-2">
                    <Button
                      size="small"
                      label="Back"
                      severity="secondary"
                      @click="activateCallback('1')"
                    />
                    <Button
                      size="small"
                      label="Next"
                      @click="activateCallback('3')"
                    />
                  </div>
                </div>
              </StepPanel>
            </StepItem>

            <StepItem value="3">
              <Step value="3">Done</Step>
              <StepPanel v-slot="{ activateCallback }" value="3">
                <div class="flex flex-col gap-4 py-4 text-zinc-300">
                  <p>You have successfully installed Mehrak to that server.</p>
                  <div class="flex gap-2">
                    <Button
                      size="small"
                      label="Back"
                      severity="secondary"
                      @click="activateCallback('2')"
                    />
                  </div>
                </div>
              </StepPanel>
            </StepItem>
          </Stepper>
        </div>

        <div class="mt-8 pt-6 border-t border-white/10">
          <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-6">
            Installing to Discord User Profile
          </h3>

          <Stepper value="1">
            <StepItem value="1">
              <Step value="1">Install Page</Step>
              <StepPanel v-slot="{ activateCallback }" value="1">
                <div class="flex flex-col gap-4 py-4 text-zinc-300">
                  <p>
                    Click this link to open the
                    <a
                      href="https://discord.com/oauth2/authorize?client_id=1365154828430610532"
                      target="_blank"
                      rel="noopener noreferrer"
                      class="text-emerald-400 hover:text-emerald-300 transition-colors"
                      >Mehrak install page</a
                    >
                    and select
                    <strong class="text-zinc-200">Install to account</strong>.
                  </p>
                  <div class="flex gap-2">
                    <Button
                      size="small"
                      label="Next"
                      @click="activateCallback('2')"
                    />
                  </div>
                </div>
              </StepPanel>
            </StepItem>

            <StepItem value="2">
              <Step value="2">Done</Step>
              <StepPanel v-slot="{ activateCallback }" value="2">
                <div class="flex flex-col gap-4 py-4 text-zinc-300">
                  <p>
                    You have successfully installed Mehrak to your user account.
                    You can now use Mehrak in any Discord server with External
                    Applications enabled, or in DMs.
                  </p>
                  <div class="flex gap-2">
                    <Button
                      size="small"
                      label="Back"
                      severity="secondary"
                      @click="activateCallback('1')"
                    />
                  </div>
                </div>
              </StepPanel>
            </StepItem>
          </Stepper>
        </div>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #title>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 m-0">
          Using Commands
        </h3>
      </template>
      <template #content>
        <p class="text-zinc-300">
          Mehrak uses Discord slash commands. After successful installation, you
          can access the command list by typing
          <code
            class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
            >/</code
          >
          in supported text channels.
        </p>
      </template>
    </Card>

    <Card id="adding-a-profile" class="bg-white/5 border border-white/10 rounded-2xl">
      <template #title>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 m-0">
          Adding a Profile
        </h3>
      </template>
      <template #content>
        <div class="flex flex-col gap-4">
          <p class="text-zinc-300">
            To use game-related commands, you must first add a profile to
            Mehrak. Run the following command:
          </p>

          <pre
            class="bg-black/40 border border-white/10 text-[#8affc1] p-4 rounded-lg overflow-x-auto font-mono"
          ><code>/profile add</code></pre>

          <p class="text-zinc-300">
            After running Profile Add, you will be prompted with an
            authentication modal with the following inputs:
          </p>
          <ul class="list-disc list-inside text-zinc-300 space-y-1">
            <li>HoYoLAB UID</li>
            <li>HoYoLAB Cookies</li>
            <li>Passphrase</li>
          </ul>

          <Message severity="info" :closable="false">
            You are advised to use a computer to complete authentication, as
            these steps are easier to perform on desktop.
          </Message>

          <p class="text-zinc-300">
            You can find this information using the steps below.
          </p>
        </div>

        <div class="mt-8 pt-6 border-t border-white/10">
          <h4 class="text-xl font-bold text-zinc-100 mb-4">HoYoLAB UID</h4>

          <Stepper value="1">
            <StepItem value="1">
              <Step value="1">Login</Step>
              <StepPanel v-slot="{ activateCallback }" value="1">
                <div class="flex flex-col gap-4 py-4 text-zinc-300">
                  <p>
                    Go to
                    <a
                      href="https://www.hoyolab.com/"
                      target="_blank"
                      rel="noopener noreferrer"
                      class="text-emerald-400 hover:text-emerald-300 transition-colors"
                      >HoYoLAB</a
                    >
                    and sign in with your HoYoverse account.
                  </p>
                  <div class="flex gap-2">
                    <Button
                      size="small"
                      label="Next"
                      @click="activateCallback('2')"
                    />
                  </div>
                </div>
              </StepPanel>
            </StepItem>

            <StepItem value="2">
              <Step value="2">Profile Page</Step>
              <StepPanel v-slot="{ activateCallback }" value="2">
                <div class="flex flex-col gap-4 py-4 text-zinc-300">
                  <p>
                    Click your profile picture in the top-right corner and
                    select
                    <strong class="text-zinc-200">Personal Homepage</strong>.
                  </p>
                  <div class="flex gap-2">
                    <Button
                      size="small"
                      label="Back"
                      severity="secondary"
                      @click="activateCallback('1')"
                    />
                    <Button
                      size="small"
                      label="Next"
                      @click="activateCallback('3')"
                    />
                  </div>
                </div>
              </StepPanel>
            </StepItem>

            <StepItem value="3">
              <Step value="3">Copy UID</Step>
              <StepPanel v-slot="{ activateCallback }" value="3">
                <div class="flex flex-col gap-4 py-4 text-zinc-300">
                  <p>
                    Your UID is the numeric value after
                    <code
                      class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
                      >id=</code
                    >
                    in the URL.
                  </p>
                  <figure>
                    <Image
                      src="/docs/getting-started/hoyolab-uid-url-example.png"
                      alt="HoYoLAB UID in browser URL"
                      preview
                      class="rounded-lg border border-white/10 overflow-hidden block bg-black/20 w-full"
                      imageClass="w-full h-auto block"
                    >
                      <template #previewicon>
                        <i class="pi pi-search"></i>
                      </template>
                    </Image>
                  </figure>
                  <p>
                    Copy this number and paste it into the UID input in the
                    authentication modal.
                  </p>
                  <p>In this example, the HoYoLAB UID is 36639475.</p>
                  <div class="flex gap-2">
                    <Button
                      size="small"
                      label="Back"
                      severity="secondary"
                      @click="activateCallback('2')"
                    />
                  </div>
                </div>
              </StepPanel>
            </StepItem>
          </Stepper>
        </div>

        <div class="mt-8 pt-6 border-t border-white/10">
          <h4 class="text-xl font-bold text-zinc-100 mb-4">HoYoLAB Cookies</h4>

          <div class="flex flex-col gap-4">
            <p class="text-zinc-300">
              Mehrak requires your HoYoLAB cookies to retrieve in-game
              information for other commands. You are strongly advised to read
              the
              <a
                href="#"
                @click.prevent="router.push('/docs?tab=appendix&section=cookies')"
                class="text-emerald-400 hover:text-emerald-300 transition-colors"
                >About HoYoLAB Cookies</a
              >
              page before providing this data.
            </p>

            <Stepper value="1">
              <StepItem value="1">
                <Step value="1">Login</Step>
                <StepPanel v-slot="{ activateCallback }" value="1">
                  <div class="flex flex-col gap-4 py-4 text-zinc-300">
                    <p>
                      Sign in to HoYoLAB with the same account used to retrieve
                      your HoYoLAB UID.
                    </p>
                    <div class="flex gap-2">
                      <Button
                        size="small"
                        label="Next"
                        @click="activateCallback('2')"
                      />
                    </div>
                  </div>
                </StepPanel>
              </StepItem>

              <StepItem value="2">
                <Step value="2">Open DevTools</Step>
                <StepPanel v-slot="{ activateCallback }" value="2">
                  <div class="flex flex-col gap-4 py-4 text-zinc-300">
                    <p>
                      Press
                      <code
                        class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
                        >Ctrl</code
                      >
                      +
                      <code
                        class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
                        >Shift</code
                      >
                      +
                      <code
                        class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
                        >I</code
                      >.
                    </p>
                    <div class="flex gap-2">
                      <Button
                        size="small"
                        label="Back"
                        severity="secondary"
                        @click="activateCallback('1')"
                      />
                      <Button
                        size="small"
                        label="Next"
                        @click="activateCallback('3')"
                      />
                    </div>
                  </div>
                </StepPanel>
              </StepItem>

              <StepItem value="3">
                <Step value="3">Copy Cookies</Step>
                <StepPanel v-slot="{ activateCallback }" value="3">
                  <div class="flex flex-col gap-4 py-4 text-zinc-300">
                    <p>
                      Browser menus may differ, but use these general steps and
                      copy the value for
                      <code
                        class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
                        >ltoken_v2</code
                      >.
                    </p>

                    <div>
                      <p class="text-zinc-200 font-semibold mb-2">
                        Chromium-based browsers (Chrome, Edge, Opera, Brave):
                      </p>
                      <p class="pl-4">
                        Open
                        <strong class="text-zinc-200">Application</strong>
                        tab, select
                        <strong class="text-zinc-200"
                          >Storage &gt; Cookies</strong
                        >, choose the site, then copy
                        <code
                          class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
                          >ltoken_v2</code
                        >.
                      </p>
                    </div>

                    <figure>
                      <Image
                        src="/docs/getting-started/cookies-chromium-devtools.png"
                        alt="Chromium developer tools cookies panel"
                        preview
                        class="rounded-lg border border-white/10 overflow-hidden block bg-black/20 w-full"
                        imageClass="w-full h-auto block"
                      >
                        <template #previewicon>
                          <i class="pi pi-search"></i> </template
                      ></Image>
                      <figcaption class="mt-2 text-sm text-zinc-500">
                        Developer console for Chromium-based browser.
                      </figcaption>
                    </figure>

                    <div>
                      <p class="text-zinc-200 font-semibold mb-2">
                        Firefox-based browsers (Firefox, LibreWolf):
                      </p>
                      <p class="pl-4">
                        Open
                        <strong class="text-zinc-200">Storage</strong> tab,
                        select <strong class="text-zinc-200">Cookies</strong>,
                        choose the site, then copy
                        <code
                          class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
                          >ltoken_v2</code
                        >.
                      </p>
                    </div>

                    <figure>
                      <Image
                        src="/docs/getting-started/cookies-firefox-devtools.png"
                        alt="Firefox developer tools cookies panel"
                        preview
                        class="rounded-lg border border-white/10 overflow-hidden block bg-black/20 w-full"
                        imageClass="w-full h-auto block"
                      >
                        <template #previewicon>
                          <i class="pi pi-search"></i> </template
                      ></Image>
                      <figcaption class="mt-2 text-sm text-zinc-500">
                        Developer console for Firefox-based browser.
                      </figcaption>
                    </figure>

                    <div class="flex gap-2">
                      <Button
                        size="small"
                        label="Back"
                        severity="secondary"
                        @click="activateCallback('2')"
                      />
                      <Button
                        size="small"
                        label="Next"
                        @click="activateCallback('4')"
                      />
                    </div>
                  </div>
                </StepPanel>
              </StepItem>

              <StepItem value="4">
                <Step value="4">Provide Cookie</Step>
                <StepPanel v-slot="{ activateCallback }" value="4">
                  <div class="flex flex-col gap-4 py-4 text-zinc-300">
                    <p>
                      Paste the copied value into the cookie input. Your cookie
                      should begin with
                      <code
                        class="bg-white/10 px-1.5 py-0.5 rounded font-mono text-sm text-zinc-200"
                        >v2_C...</code
                      >.
                    </p>
                    <div class="flex gap-2">
                      <Button
                        size="small"
                        label="Back"
                        severity="secondary"
                        @click="activateCallback('3')"
                      />
                    </div>
                  </div>
                </StepPanel>
              </StepItem>
            </Stepper>
          </div>
        </div>

        <div class="mt-8 pt-6 border-t border-white/10">
          <h4 class="text-xl font-bold text-zinc-100 mb-4">Passphrase</h4>

          <div class="flex flex-col gap-4 text-zinc-300">
            <p>
              To protect stored cookies, Mehrak requires a passphrase that is
              used to encrypt your cookie in the database. A passphrase should
              be easy for humans to remember, but difficult for machines to
              crack.
            </p>

            <div>
              <p class="mb-2">Recommended passphrase practices:</p>
              <ul class="list-disc list-inside space-y-1">
                <li>
                  Choose a sentence you can remember, such as a quote from a
                  movie, book, or game character.
                </li>
                <li>Use a long sentence, ideally at least 20 characters.</li>
                <li>
                  Do not reuse passphrases from other services or profiles.
                </li>
              </ul>
            </div>

            <p>
              The passphrase is used verbatim for encryption, including leading
              or trailing spaces, line breaks, and paragraph breaks.
            </p>

            <p>
              Mehrak does not store passphrases in the database. Your passphrase
              is required for commands that use HoYoLAB cookies. For
              convenience, after authentication, decrypted cookies are cached
              for 5 minutes before active eviction, allowing additional commands
              without re-entering the passphrase.
            </p>
          </div>
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
/* Stepper content overrides for consistency inside standard PrimeVue components */
:deep(.p-steppanel) {
  padding-left: 0.5rem;
}
:deep(.p-stepitem) {
  padding-bottom: 0.5rem;
}
</style>
