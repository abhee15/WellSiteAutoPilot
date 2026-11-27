import { expect, it } from 'vitest';
import {
  addCurrentInput,
  addDailyInput,
  blankContractManifest,
  inputName,
  sourceOptionsFrom,
} from '../../src/views/contractManifest';

function operation(capabilityId: string, quantity: string, unit: string | null) {
  return {
    capability: 'CurrentData',
    capabilityId,
    quantity,
    unit,
    structuredSchemaId: null,
    structuredJsonSchema: null,
  };
}

it('adds a discovered production-day field as an uncertain-quality daily input, once', () => {
  const blank = blankContractManifest('Daily check');
  const added = addDailyInput(blank, 'CyclesYesterday', 'count');
  const input = (JSON.parse(added) as { dataRequirements: Record<string, unknown>[] }).dataRequirements.at(0);
  expect(input).toMatchObject({
    quantity: 'CyclesYesterday',
    canonicalUnit: 'count',
    access: 'Current',
    allowUncertainQuality: true,
    requiredCapability: 'engineering.daily',
    maximumAgeSeconds: 172800,
  });
  expect((input?.valueSchema as { schema: { type: string } }).schema.type).toBe('number');
  // A field already declared as a daily input is not duplicated, and a name that is not an identifier is refused.
  expect(addDailyInput(added, 'CyclesYesterday', 'count')).toBe(added);
  expect(addDailyInput(added, 'Cycles Yesterday', 'count')).toBe(added);
  expect(
    (JSON.parse(addDailyInput(added, 'RunTime', 'h')) as { dataRequirements: unknown[] }).dataRequirements,
  ).toHaveLength(2);
});

it('names a daily value apart from a current reading of the same field', () => {
  const blank = blankContractManifest('Both');
  const current = addCurrentInput(blank, {
    capabilityId: 'engineering.current',
    quantity: 'MotorAmps',
    unit: 'A',
  } as never);
  const both = addDailyInput(current, 'MotorAmps', 'A');
  const ports = (JSON.parse(both) as { dataRequirements: Record<string, unknown>[] }).dataRequirements;
  expect(ports.map(inputName)).toEqual(['MotorAmps', 'DailyMotorAmps']);
  expect(ports[1]).toMatchObject({ quantity: 'MotorAmps', alias: 'DailyMotorAmps' });
  // A field with no clash keeps its plain name.
  expect(addDailyInput(both, 'RunTime', 'h')).toContain('"quantity":"RunTime"');
  expect(
    inputName(
      (JSON.parse(addDailyInput(both, 'RunTime', 'h')) as { dataRequirements: Record<string, unknown>[] })
        .dataRequirements[2]!,
    ),
  ).toBe('RunTime');
});

it('keeps a name that is both a current reading and a daily value as two options', () => {
  const options = sourceOptionsFrom([
    {
      adapterKey: 'foresite',
      displayName: 'ForeSite',
      operations: [operation('engineering.current', 'THP', 'psia'), operation('engineering.daily', 'THP', 'psia')],
    },
  ] as never);
  expect(options.map((option) => [option.capabilityId, option.quantity, option.unit])).toEqual([
    ['engineering.current', 'THP', 'psia'],
    ['engineering.daily', 'THP', 'psia'],
  ]);
});
