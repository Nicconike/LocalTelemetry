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
            motherboardMfr: 'TestBoardVendor',
            motherboardModel: 'Test Board Model Pro',
            motherboardVersion: 'Rev 1.0',
            motherboardSerial: 'SN-12345678',
            bios: 'AMI 2101',
            biosUefi: true,
            psu: 'Test PSU Titanium 1000W',
            psuCapacity: '1000W Titanium',
            cpu: 'Test CPU 24-Core Model',
            cpuVendor: 'TestCpuVendor',
            cpuCores: 24,
            cpuThreads: 32,
            cpuBaseSpeedMhz: 3200,
            cpuMaxSpeedMhz: 6200,
            cpuSocket: 'LGA1700',
            cpuTdpWatts: 150,
            installedRamGb: 64,
            ramGb: 63.8,
            ramMfr: 'TestRamVendor',
            ramSpeed: '6000 MT/s',
            ramType: 'DDR5',
            ramSlots: 4,
            ramModules: [
                { sizeGb: 32, speed: '6000 MT/s' },
                { sizeGb: 32, speed: '6000 MT/s' }
            ],
            gpus: [
                {
                    name: 'Test GPU Dedicated 24GB',
                    dedicated: true,
                    vramGb: 24,
                    driver: '560.94',
                    tdpW: 450
                },
                {
                    name: 'Test GPU Integrated',
                    dedicated: false,
                    vramGb: 0,
                    driver: '31.0.101',
                    tdpW: 15
                }
            ],
            disks: [
                {
                    model: 'Test NVMe SSD 2TB',
                    boot: true,
                    formattedSize: '1.81 TB',
                    displayType: 'NVMe SSD',
                    busType: 'NVMe'
                },
                {
                    model: 'Test SATA SSD 4TB',
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
            nics: ['Test Ethernet Adapter 2.5GbE', 'Test Wi-Fi Adapter']
        } as any);

        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('TestBoardVendor Test Board Model Pro');
        expect(document.body.textContent).toContain('UEFI');
        expect(document.body.textContent).toContain('Test PSU Titanium 1000W');
        expect(document.body.textContent).toContain('3.2 GHz');
        expect(document.body.textContent).toContain('6.2 GHz');
        expect(document.body.textContent).toContain('64.0 GB');
        expect(document.body.textContent).toContain('63.8 GB usable');
        expect(document.body.textContent).toContain('TestRamVendor');
        expect(document.body.textContent).toContain('Module 1');
        expect(document.body.textContent).toContain('Test GPU Dedicated 24GB');
        expect(document.body.textContent).toContain('Dedicated');
        expect(document.body.textContent).toContain('Integrated');
        expect(document.body.textContent).toContain('Test NVMe SSD 2TB');
        expect(document.body.textContent).toContain('Boot');
        expect(document.body.textContent).toContain('[NVMe SSD]');
        expect(document.body.textContent).toContain('Test Ethernet Adapter 2.5GbE');
    });

    it('renders laptop with battery specifications and legacy bios mode', async () => {
        systemInfo.set({
            os: 'Windows 11 Home',
            deviceName: 'LAPTOP-ZEN',
            systemType: 'Laptop',
            motherboardModel: 'Test Laptop Board',
            biosUefi: false,
            cpu: 'Test Mobile CPU',
            ramGb: 32,
            batteryManufacturer: 'TestBatteryVendor',
            batteryDeviceName: 'TEST-BATTERY-01',
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
        expect(document.body.textContent).toContain('Test Laptop Board');
        expect(document.body.textContent).toContain('Legacy');
        expect(document.body.textContent).toContain('Battery');
        expect(document.body.textContent).toContain('TestBatteryVendor');
        expect(document.body.textContent).toContain('TEST-BATTERY-01');
        expect(document.body.textContent).toContain('75000 mWh');
        expect(document.body.textContent).toContain('98%');
        expect(document.body.textContent).toContain('2%');
        expect(document.body.textContent).toContain('42 cycle(s)');
    });

    it('renders storage bus types and handles missing memory, cpu, and gpu optional fields', async () => {
        systemInfo.set({
            os: 'Windows 11 Home',
            deviceName: 'MIN-PC',
            cpu: 'Test Entry CPU',
            installedRamGb: 16,
            ramGb: 0,
            gpus: [
                {
                    name: 'Test Basic GPU',
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
            cpu: 'Test CPU Mobile Pro',
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
            motherboardMfr: 'TestBoardMfr',
            motherboardModel: '',
            cpu: 'Test CPU Model A',
            disks: [],
            gpus: [],
            nics: []
        } as any);

        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('Desktop');
        expect(document.body.textContent).toContain('TestBoardMfr');

        // Case 2: motherboardModel only (no mfr)
        systemInfo.set({
            os: 'Windows 11',
            systemType: 'Workstation',
            motherboardMfr: '',
            motherboardModel: 'TestModel-Plus',
            cpu: 'Test CPU Model B',
            disks: [],
            gpus: [],
            nics: []
        } as any);
        await tick();

        expect(document.body.textContent).toContain('Workstation');
        expect(document.body.textContent).toContain('TestModel-Plus');
    });

    it('renders storage health, temperature, remaining life, and reads/writes', async () => {
        systemInfo.set({
            os: 'Test OS 11',
            cpu: 'Generic Test CPU @ 3.00GHz',
            deviceName: 'TEST-DEVICE',
            disks: [
                {
                    model: 'NVMe Test Drive Alpha 1TB',
                    boot: true,
                    formattedSize: '1 TB',
                    displayType: 'NVMe SSD',
                    health: 'Healthy',
                    remainingLifePercent: 99,
                    temperatureC: 48,
                    totalWrittenTb: '16.5',
                    totalReadTb: '14.6',
                },
                {
                    model: 'SATA Test Drive Beta 2TB',
                    boot: false,
                    formattedSize: '2 TB',
                    displayType: 'SATA SSD',
                    health: 'Warning',
                    remainingLifePercent: 91,
                    temperatureC: 40,
                    totalWrittenTb: '60.1',
                    totalReadTb: '71.6',
                }
            ],
            gpus: [],
            nics: [],
        } as any);

        component = mount(SystemPage, { target: document.body });
        await tick();

        expect(document.body.textContent).toContain('NVMe Test Drive Alpha 1TB');
        expect(document.body.textContent).toContain('Healthy');
        expect(document.body.textContent).toContain('99% life');
        expect(document.body.textContent).toContain('Total Writes: 16.5 TBW');
        expect(document.body.textContent).toContain('Total Reads: 14.6 TBR');

        expect(document.body.textContent).toContain('Warning');
        expect(document.body.textContent).toContain('91% life');
        expect(document.body.textContent).toContain('Total Writes: 60.1 TBW');
        expect(document.body.textContent).toContain('Total Reads: 71.6 TBR');
    });
});
