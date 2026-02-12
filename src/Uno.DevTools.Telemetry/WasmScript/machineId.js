// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/**
 * Gets or creates a persistent machine ID using browser localStorage
 * This function is called from .NET via JSImport
 * @returns {string} The machine ID (GUID format)
 */
globalThis.unoDevToolsGetOrCreateMachineId = function() {
    const storageKey = 'uno.devtools.telemetry.machineId';

    try {
        // Try to get existing machine ID from localStorage
        let machineId = localStorage.getItem(storageKey);

        if (!machineId) {
            // Generate a new GUID
            machineId = generateGuid();

            // Store it for future sessions
            localStorage.setItem(storageKey, machineId);
        }

        return machineId;
    } catch (error) {
        // If localStorage is not available (privacy mode, disabled, etc.)
        // fall back to generating a session-specific GUID
        console.warn('localStorage not available for telemetry machine ID:', error);
        return generateGuid();
    }
};

/**
 * Generates a GUID in standard format (xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx)
 * @returns {string} A new GUID
 */
function generateGuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
        const r = Math.random() * 16 | 0;
        const v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}
