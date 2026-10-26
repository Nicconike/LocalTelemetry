import { mount, unmount, tick } from 'svelte';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { systemInfo } from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/lib/store';
import SystemPage from '../../../../../../../src/LocalTelemetry.App/Settings/wwwroot/src/components/pages/SystemPage.svelte';

describe('SystemPage.svelte', () => {
    let component: ReturnType<typeof mount> | undefined;

    beforeEach(() => {
        document.body.innerHTML = '';
    });

    afterEach(() => {
        if (component) {
            unmount(component);
            component = undefined;
        }
    });

    it('renders loading state when info or os is undefined', async () => {
        systemInfo.set(null as any);
        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Detecting hardware');
    });

    it('renders system information details with empty devices', async () => {
        systemInfo.set({
            os: 'Windows 11 Pro',
            cpu: 'Intel Core i9-13900K',
            deviceName: 'DESKTOP-LT',
            ramTotalMb: 32768,
            disks: [],
            gpus: [],
            nics: [],
        } as any);

        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('System');
        expect(document.body.textContent).toContain('Windows 11 Pro');
        expect(document.body.textContent).toContain('Intel Core i9-13900K');
        expect(document.body.textContent).toContain('None detected');
    });

    it('renders full desktop motherboard, bios, psu, cpu, ram, gpu, storage, and network details', async () => {
        systemInfo.set({
            os: 'Windows 11 Enterprise',
            deviceName: 'RIG-ALPHA',
            systemType: 'Desktop',
            systemModel: 'Custom Workstation',
            motherboardMfr: 'ASUS',
            motherboardModel: 'ROG MAXIMUS Z790',
            motherboardVersion: 'Rev 1.0',
            motherboardSerial: 'SN-12345678',
            bios: 'AMI 2101',
            biosUefi: true,
            psu: 'Corsair AX1600i',
            psuCapacity: '1600W Titanium',
            cpu: 'Intel Core i9-14900KS',
            cpuVendor: 'GenuineIntel',
            cpuCores: 24,
            cpuThreads: 32,
            cpuBaseSpeedMhz: 3200,
            cpuMaxSpeedMhz: 6200,
            cpuSocket: 'LGA1700',
            cpuTdpWatts: 150,
            installedRamGb: 64,
            ramGb: 63.8,
            ramMfr: 'G.Skill',
            ramSpeed: '6000 MT/s',
            ramType: 'DDR5',
            ramSlots: 4,
            ramModules: [
                { sizeGb: 32, speed: '6000 MT/s' },
                { sizeGb: 32, speed: '6000 MT/s' }
            ],
            gpus: [
                {
                    name: 'NVIDIA GeForce RTX 4090',
                    dedicated: true,
                    vramGb: 24,
                    driver: '560.94',
                    tdpW: 450
                },
                {
                    name: 'Intel UHD Graphics 770',
                    dedicated: false,
                    vramGb: 0,
                    driver: '31.0.101',
                    tdpW: 15
                }
            ],
            disks: [
                {
                    model: 'Samsung 990 PRO 2TB',
                    boot: true,
                    formattedSize: '1.81 TB',
                    displayType: 'NVMe SSD',
                    busType: 'NVMe'
                },
                {
                    model: 'Crucial MX500 4TB',
                    boot: false,
                    sizeGb: 4000,
                    busType: 'SCSI'
                },
                {
                    model: 'Generic USB Storage',
                    boot: false,
                    sizeGb: 128,
                    busType: 'BusTypeUsb'
                },
                {
                    model: 'Standard Disk',
                    boot: false,
                    sizeGb: 500,
                    busType: 'DISK'
                },
                {
                    model: 'Unknown Bus Drive',
                    boot: false,
                    sizeGb: 250,
                    busType: ''
                },
                {
                    model: 'Bare Drive',
                    boot: false
                },
                {
                    model: 'Bus Only Drive',
                    boot: false,
                    busType: 'SATA'
                },
                {
                    model: 'Display Type Only Drive',
                    boot: false,
                    displayType: 'External SSD'
                }
            ],
            nics: ['Intel I226-V 2.5GbE', 'Intel Wi-Fi 7 BE200']
        } as any);

        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('ASUS ROG MAXIMUS Z790');
        expect(document.body.textContent).toContain('UEFI');
        expect(document.body.textContent).toContain('Corsair AX1600i');
        expect(document.body.textContent).toContain('3.2 GHz');
        expect(document.body.textContent).toContain('6.2 GHz');
        expect(document.body.textContent).toContain('64.0 GB');
        expect(document.body.textContent).toContain('63.8 GB usable');
        expect(document.body.textContent).toContain('G.Skill');
        expect(document.body.textContent).toContain('Module 1');
        expect(document.body.textContent).toContain('NVIDIA GeForce RTX 4090');
        expect(document.body.textContent).toContain('Dedicated');
        expect(document.body.textContent).toContain('Integrated');
        expect(document.body.textContent).toContain('Samsung 990 PRO 2TB');
        expect(document.body.textContent).toContain('Boot');
        expect(document.body.textContent).toContain('[NVMe SSD]');
        expect(document.body.textContent).toContain('Intel I226-V 2.5GbE');
    });

    it('renders laptop with battery specifications and legacy bios mode', async () => {
        systemInfo.set({
            os: 'Windows 11 Home',
            deviceName: 'LAPTOP-ZEN',
            systemType: 'Laptop',
            motherboardModel: 'ZenBook UX8406',
            biosUefi: false,
            cpu: 'AMD Ryzen 9 8945HS',
            ramGb: 32,
            batteryManufacturer: 'SMP',
            batteryDeviceName: 'UX8406-BAT',
            batteryDesignCapacity: '75000 mWh',
            batteryFullChargedCapacity: '73500 mWh',
            batteryHealthPct: 98,
            batteryWearLevelPct: 2,
            batteryCycleCount: 42,
            disks: [],
            gpus: [],
            nics: []
        } as any);

        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Laptop');
        expect(document.body.textContent).toContain('ZenBook UX8406');
        expect(document.body.textContent).toContain('Legacy');
        expect(document.body.textContent).toContain('Battery');
        expect(document.body.textContent).toContain('SMP');
        expect(document.body.textContent).toContain('UX8406-BAT');
        expect(document.body.textContent).toContain('75000 mWh');
        expect(document.body.textContent).toContain('98%');
        expect(document.body.textContent).toContain('2%');
        expect(document.body.textContent).toContain('42 cycle(s)');
    });

    it('renders storage bus types and handles missing memory, cpu, and gpu optional fields', async () => {
        systemInfo.set({
            os: 'Windows 11 Home',
            deviceName: 'MIN-PC',
            cpu: 'Intel Processor N100',
            installedRamGb: 16,
            ramGb: 0,
            gpus: [
                {
                    name: 'Intel UHD Graphics',
                    dedicated: false,
                }
            ],
            disks: [
                { model: 'Disk SATA', sizeGb: 500, busType: 'SATA' },
                { model: 'Disk SAS', sizeGb: 600, busType: 'SAS' },
                { model: 'Disk ATAPI', sizeGb: 20, busType: 'ATAPI' },
                { model: 'Disk RAID', sizeGb: 2000, busType: 'RAID' },
                { model: 'Disk iSCSI', sizeGb: 4000, busType: 'iSCSI' },
                { model: 'Disk Virtual', sizeGb: 100, busType: 'FileBackedVirtual' },
                { model: 'Disk SD', sizeGb: 64, busType: 'SD' },
                { model: 'Disk MMC', sizeGb: 32, busType: 'MMC' },
                { model: 'Disk Max', sizeGb: 50, busType: 'BusTypeMax' },
                { model: 'Disk Plain', sizeGb: 10 }
            ],
            nics: []
        } as any);

        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('16.0 GB');
        expect(document.body.textContent).toContain('[SATA]');
        expect(document.body.textContent).toContain('[SAS]');
        expect(document.body.textContent).toContain('[ATAPI]');
        expect(document.body.textContent).toContain('[RAID]');
        expect(document.body.textContent).toContain('[ISCSI]');
        expect(document.body.textContent).toContain('[FILEBACKEDVIRTUAL]');
        expect(document.body.textContent).toContain('[SD]');
        expect(document.body.textContent).toContain('[MMC]');
    });

    it('renders laptop without battery optional fields, and covers memory and disk edge branches', async () => {
        systemInfo.set({
            os: 'Windows 11 Pro',
            systemType: 'Laptop',
            cpu: 'Core Ultra 7',
            installedRamGb: 0,
            ramGb: 16,
            ramModules: [{ sizeGb: 16, speed: '' }],
            disks: [
                {
                    model: 'Only Formatted Disk',
                    formattedSize: '512 GB',
                    displayType: 'SSD',
                },
                {
                    model: 'Only SizeGb and BusType Disk',
                    sizeGb: 256,
                    busType: 'NVMe',
                },
                {
                    model: 'Completely Bare Disk',
                },
                {
                    model: undefined,
                }
            ],
            gpus: [
                {
                    name: 'Bare GPU',
                    dedicated: false,
                    vramGb: 0,
                    driver: '',
                    tdpW: 0,
                }
            ],
            nics: []
        } as any);

        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Laptop');
        expect(document.body.textContent).toContain('16 GB');
        expect(document.body.textContent).toContain('(512 GB)');
        expect(document.body.textContent).toContain('[SSD]');
        expect(document.body.textContent).toContain('(256 GB)');
        expect(document.body.textContent).toContain('[NVME]');
        expect(document.body.textContent).toContain('Completely Bare Disk');
    });

    it('covers motherboard mfr and model variations and systemType fallback', async () => {
        // Case 1: motherboardMfr only (no model), empty systemType (defaults to Desktop)
        systemInfo.set({
            os: 'Windows 11',
            systemType: '',
            motherboardMfr: 'MSI',
            motherboardModel: '',
            cpu: 'AMD Ryzen 5',
            disks: [],
            gpus: [],
            nics: []
        } as any);

        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Desktop');
        expect(document.body.textContent).toContain('MSI');

        // Case 2: motherboardModel only (no mfr)
        systemInfo.set({
            os: 'Windows 11',
            systemType: 'Workstation',
            motherboardMfr: '',
            motherboardModel: 'B650M-PLUS',
            cpu: 'AMD Ryzen 7',
            disks: [],
            gpus: [],
            nics: []
        } as any);
        await tick();

        expect(document.body.textContent).toContain('Workstation');
        expect(document.body.textContent).toContain('B650M-PLUS');
    });
});
