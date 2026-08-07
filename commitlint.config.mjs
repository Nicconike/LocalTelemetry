const DEPENDABOT = /Signed-off-by: dependabot\[bot\]/
const HUMAN_LIMIT = 200
const DEPENDABOT_LIMIT = 400

const bodyMaxLineLength = (parsed) => {
    const isDependabot = DEPENDABOT.test(parsed?.raw ?? '')
    const limit = isDependabot ? DEPENDABOT_LIMIT : HUMAN_LIMIT
    const body = parsed?.body
    if (!body) return [true]
    const ok = body.split(/\r?\n/).every(
        (line) => line.length <= limit || /https?:\/\/\S+/.test(line)
    )
    return [ok, `body's lines must not be longer than ${limit} characters`]
}

export default {
    extends: ['@commitlint/config-conventional'],
    plugins: ['commitlint-plugin-function-rules'],
    rules: {
        'subject-case': [2, 'never', ['upper-case']],
        'body-max-line-length': [0],
        'function-rules/body-max-line-length': [2, 'always', bodyMaxLineLength],
    },
}
